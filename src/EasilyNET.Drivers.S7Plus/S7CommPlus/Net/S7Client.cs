// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.S7Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Net;

// Teilweise basierend auf Snap7 (Sharp7.cs) von Davide Nardella
// |  Sharp7 is free software: you can redistribute it and/or modify              |
// |  it under the terms of the Lesser GNU General Public License as published by |
// |  the Free Software Foundation, either version 3 of the License, or           |
// |  (at your option) any later version.                                         |
internal sealed class S7Client : IConnectorCallback, IDisposable
{
    #region [Constants and TypeDefs]

    private int _LastError;

    #endregion

    #region [S7 Telegrams]

    // ISO Connection Request telegram (contains also ISO Header and COTP Header)
    private readonly byte[] ISO_CR = [
			// TPKT (RFC1006 Header)
			0x03, // RFC 1006 ID (3) 
			0x00, // Reserved, always 0
			0x00, // High part of packet lenght (entire frame, payload and TPDU included)
			0x24, // Low part of packet lenght (entire frame, payload and TPDU included)
			// COTP (ISO 8073 Header)
			0x1f, // PDU Size Length
			0xE0, // CR - Connection Request ID
			0x00, // Dst Reference HI
			0x00, // Dst Reference LO
			0x00, // Src Reference HI
			0x01, // Src Reference LO
			0x00, // Class + Options Flags
			0xC0, // PDU Max Length ID
			0x01, // PDU Max Length HI
			0x0A, // PDU Max Length LO
			0xC1, // Src TSAP Identifier
			0x02, // Src TSAP Length (2 bytes)
			0x01, // Src TSAP HI (will be overwritten)
			0x00, // Src TSAP LO (will be overwritten)
			0xC2, // Dst TSAP Identifier
			0x10, // Dst TSAP Length (16 bytes)
			// Ab hier TSAP ID (String)
			// SIMATIC-ROOT-HMI
		];

    // TPKT + ISO COTP Header (Connection Oriented Transport Protocol)
    private readonly byte[] TPKT_ISO = [ // 7 bytes
			0x03,0x00,
            0x00,0x1f,      // Telegram Length (Data Size + 31 or 35)
			0x02,0xf0,0x80  // COTP (see above for info)
		];

    #endregion

    #region S7commPlus

    private bool m_SslActive;
    private Thread? m_runThread;
    private bool m_runThread_DoStop;
    private S7TlsConnector? m_sslconn;
    private readonly ILogger log;
    private bool m_disposed;

    // OpenSSL möchte Daten auf den Socket aussenden.
    public void WriteData(byte[] pData, int dataLength)
    {
        // SSL fordert Daten zum Absenden an
        // TODO: Was ist, wenn SSL Daten verschicken möchte, die größer als eine TPDU sind?
        // Bei großen Zertifikaten oder ähnlichem? Fragmentierung hier?
        // Trace.WriteLine("S7Client - OpenSSL WriteData: dataLength=" + dataLength);
        var sendData = new byte[dataLength];
        Array.Copy(pData, sendData, dataLength);
        SendIsoPacket(sendData);
    }

    // OpenSSL meldet fertige Daten (decrypted) zum einlesen
    public void OnDataAvailable()
    {
        // 回调发生时 TLS 连接器必然已建立；此判空仅为消除可空告警并防御异常时序
        if (m_sslconn is null)
        {
            return;
        }
        // Netzwerk meldet eintreffende Daten
        var buf = new byte[8192];
        var bytesRead = m_sslconn.Receive(ref buf, buf.Length);
        // Trace.WriteLine("S7Client - OpenSSL OnDataAvailable: bytesRead=" + bytesRead);
        var readData = new byte[bytesRead];
        Array.Copy(buf, readData, bytesRead);
        OnDataReceived?.Invoke(readData, bytesRead);
    }

    // 启动 TLS：创建 BouncyCastle 连接器并立即发出 ClientHello。
    // 强制 TLS 1.3 + AES-GCM，因为 S7CommPlus on IsoOnTCP 依赖加密后固定（+17 字节）的长度增量来分片。
    public int SslActivate()
    {
        try
        {
            m_sslconn = new S7TlsConnector(this);
            // 先置位，确保服务器握手应答到达时 RunThread 会路由到 TLS 解密而非明文解析
            m_SslActive = true;
            m_sslconn.ExpectConnect();
        }
        catch (Exception ex)
        {
            m_SslActive = false;
            log?.LogDebug("S7Client - SslActivate: error = " + ex.Message);
            return S7Consts.errOpenSSL;
        }
        return 0;
    }

    // Deaktiviert TLS
    public void SslDeactivate()
    {
        m_SslActive = false;
    }
    #endregion

    private void StartThread()
    {
        m_runThread_DoStop = false;
        m_runThread = new Thread(RunThread)
        {
            // 后台线程：避免接收循环在未显式 Disconnect/Dispose 时阻塞进程退出
            IsBackground = true
        };
        m_runThread.Start();
    }

    // Der Task der kontinuierlich ausgeführt wird
    private void RunThread()
    {
        int Length;
        while (!m_runThread_DoStop)
        {
            // Versuchen zu lesen
            _LastError = 0;
            Length = RecvIsoPacket();
            // TODO: Hier nur den Payload zurückgeben
            if (Length > 0)
            {
                var Buffer = new byte[Length - TPKT_ISO.Length];
                Array.Copy(PDU, TPKT_ISO.Length, Buffer, 0, Length - TPKT_ISO.Length);
                var Size = Length - TPKT_ISO.Length;
                if (m_SslActive)
                {
                    // Durch SSL eingelesene Daten an SSL weiterleiten
                    try
                    {
                        m_sslconn?.ReadCompleted(Buffer, Size);
                    }
                    catch (Exception ex)
                    {
                        // TLS 握手/解密异常（如 PLC 发回 TLS Alert）。若不捕获会静默杀死接收线程，
                        // 上层只会看到读超时(0x5)。记录后停止本线程，触发上层重连。
                        log?.LogDebug("S7Client - RunThread: TLS error = " + ex);
                        _LastError = S7Consts.errOpenSSL;
                        m_runThread_DoStop = true;
                    }
                }
                else
                {
                    // Wenn etwas gelesen werden konnte, Client benachrichtigen
                    OnDataReceived?.Invoke(Buffer, Size);
                }
            }
        }
    }

    public _OnDataReceived? OnDataReceived { get; set; }
    public delegate void _OnDataReceived(byte[] PDU, int len);

    #region [Internals]

    // Defaults
    private const int ISOTCP = 102; // ISOTCP Port
    private const int MinPduSizeToRequest = 240;
    private const int MaxPduSizeToRequest = 960;
    private const int DefaultTimeout = 2000;
    private const int IsoHSize = 7; // TPKT+COTP Header Size

    // Properties

    // Privates
    private string IPAddress = string.Empty;
    private byte LocalTSAP_HI;
    private byte LocalTSAP_LO;
    private byte[]? RemoteTSAP_S;
    private byte LastPDUType;
    private readonly byte[] PDU = new byte[2048];
    private MsgSocket? Socket;

    private void CreateSocket()
    {
        try
        {
            Socket = new MsgSocket
            {
                ConnectTimeout = ConnTimeout,
                ReadTimeout = RecvTimeout,
                WriteTimeout = SendTimeout
            };
        }
        catch
        {
        }
    }

    private int TCPConnect()
    {
        if (_LastError == 0)
        {
            try
            {
                _LastError = Socket?.Connect(IPAddress, PLCPort) ?? S7Consts.errTCPConnectionFailed;
            }
            catch
            {
                _LastError = S7Consts.errTCPConnectionFailed;
            }
        }

        return _LastError;
    }

    private void RecvPacket(byte[] Buffer, int Start, int Size)
    {
        _LastError = (Connected && Socket != null) ? Socket.Receive(Buffer, Start, Size) : S7Consts.errTCPNotConnected;
    }

    private void SendPacket(byte[] Buffer, int Len)
    {
        _LastError = Socket != null ? Socket.Send(Buffer, Len) : S7Consts.errTCPNotConnected;
    }

    private void SendPacket(byte[] Buffer)
    {
        if (Connected)
        {
            SendPacket(Buffer, Buffer.Length);
        }
        else
        {
            _LastError = S7Consts.errTCPNotConnected;
        }
    }

    public void Send(byte[] Buffer)
    {
        ArgumentNullException.ThrowIfNull(Buffer);
        if (m_SslActive)
        {
            m_sslconn?.Write(Buffer, Buffer.Length);
        }
        else
        {
            SendIsoPacket(Buffer);
        }
    }

    private int SendIsoPacket(byte[] Buffer)
    {
        // Packt die zu sendenden Daten in den Iso-Header ein.
        var Size = Buffer.Length;
        _LastError = 0;

        Array.Copy(TPKT_ISO, 0, PDU, 0, TPKT_ISO.Length);
        SetWordAt(PDU, 2, (ushort)(Size + TPKT_ISO.Length));
        try
        {
            Array.Copy(Buffer, 0, PDU, TPKT_ISO.Length, Size);
        }
        catch
        {
            return S7Consts.errIsoInvalidPDU;
        }
        SendPacket(PDU, TPKT_ISO.Length + Size);

        return _LastError;
    }

    private static ushort GetWordAt(byte[] Buffer, int Pos)
    {
        return (ushort)((Buffer[Pos] << 8) | Buffer[Pos + 1]);
    }

    private static void SetWordAt(byte[] Buffer, int Pos, ushort Value)
    {
        Buffer[Pos] = (byte)(Value >> 8);
        Buffer[Pos + 1] = (byte)(Value & 0x00FF);
    }

    private int RecvIsoPacket()
    {
        var Done = false;
        var Size = 0;
        while ((_LastError == 0) && !Done)
        {
            // Get TPKT (4 bytes)
            RecvPacket(PDU, 0, 4);
            if (_LastError == 0)
            {
                Size = GetWordAt(PDU, 2);
                // Check 0 bytes Data Packet (only TPKT+COTP = 7 bytes)
                if (Size == IsoHSize)
                {
                    RecvPacket(PDU, 4, 3); // Skip remaining 3 bytes and Done is still false
                }
                else
                {
                    // TODO: Größe korrekt prüfen
                    //if ((Size > _PduSizeRequested + IsoHSize) || (Size < MinPduSize))
                    //	_LastError = S7Consts.errIsoInvalidPDU;
                    //else
                    Done = true; // a valid Length !=7 && >16 && <247
                }
            }
        }
        if (_LastError == 0)
        {
            RecvPacket(PDU, 4, 3); // Skip remaining 3 COTP bytes
            LastPDUType = PDU[5];   // Stores PDU Type, we need it 
                                    // Receives the S7 Payload          
            RecvPacket(PDU, 7, Size - IsoHSize);
        }
        return _LastError == 0 ? Size : 0;
    }

    private int ISOConnect()
    {
        // RemoteTSAP_S 必须先经 SetConnectionParams 设置；未设置即视为连接错误
        if (RemoteTSAP_S is null)
        {
            return _LastError = S7Consts.errIsoConnect;
        }
        int Size;
        var isocon = new byte[ISO_CR.Length + RemoteTSAP_S.Length];
        ISO_CR[16] = LocalTSAP_HI;
        ISO_CR[17] = LocalTSAP_LO;

        ISO_CR[3] = (byte)(20 + RemoteTSAP_S.Length);
        ISO_CR[4] = (byte)(15 + RemoteTSAP_S.Length);
        ISO_CR[19] = (byte)RemoteTSAP_S.Length;

        Array.Copy(ISO_CR, isocon, 20);
        Array.Copy(RemoteTSAP_S, 0, isocon, 20, RemoteTSAP_S.Length);

        // Sends the connection request telegram      
        SendPacket(isocon);
        if (_LastError == 0)
        {
            // Gets the reply (if any)
            Size = RecvIsoPacket();
            if (_LastError == 0)
            {
                if (Size == 36)
                {
                    if (LastPDUType != 0xD0) // 0xD0 = CC Connection confirm
                    {
                        _LastError = S7Consts.errIsoConnect;
                    }
                }
                else
                {
                    _LastError = S7Consts.errIsoInvalidPDU;
                }
            }
        }
        return _LastError;
    }

    public byte[]? GetOMSExporterSecret()
    {
        return m_sslconn?.GetOMSExporterSecret();
    }

    #endregion

    #region [Class Control]

    public S7Client(ILogger? logger = null)
    {
        log = logger ?? NullLogger.Instance;
        CreateSocket();
    }

    ~S7Client()
    {
        Dispose(false);
    }

    public int Connect()
    {
        _LastError = 0;
        ExecutionTime = 0;
        var Elapsed = Environment.TickCount;
        if (!Connected)
        {
            TCPConnect(); // First stage : TCP Connection
            if (_LastError == 0)
            {
                ISOConnect(); // Second stage : ISOTCP (ISO 8073) Connection
                if (_LastError == 0)
                {
                    //	_LastError = S7P_InitSSLRequest(); // Third stage : Init SSL Request
                    StartThread();
                }
            }
        }
        if (_LastError != 0)
        {
            Disconnect();
        }
        else
        {
            ExecutionTime = Environment.TickCount - Elapsed;
        }

        return _LastError;
    }

    public int SetConnectionParams(string Address, ushort LocalTSAP, byte[] RemoteTSAP)
    {
        ArgumentNullException.ThrowIfNull(RemoteTSAP);
        var LocTSAP = LocalTSAP & 0x0000FFFF;
        IPAddress = Address;
        LocalTSAP_HI = (byte)(LocTSAP >> 8);
        LocalTSAP_LO = (byte)(LocTSAP & 0x00FF);

        RemoteTSAP_S = new byte[RemoteTSAP.Length];
        Array.Copy(RemoteTSAP, RemoteTSAP_S, RemoteTSAP.Length);

        return 0;
    }

    public int Disconnect()
    {
        m_runThread_DoStop = true;
        m_runThread?.Join();

        Socket?.Close();

        return 0;
    }

    public int GetParam(int ParamNumber, ref int Value)
    {
        var Result = 0;
        switch (ParamNumber)
        {
            case S7Consts.p_u16_RemotePort:
                {
                    Value = PLCPort;
                    break;
                }
            case S7Consts.p_i32_PingTimeout:
                {
                    Value = ConnTimeout;
                    break;
                }
            case S7Consts.p_i32_SendTimeout:
                {
                    Value = SendTimeout;
                    break;
                }
            case S7Consts.p_i32_RecvTimeout:
                {
                    Value = RecvTimeout;
                    break;
                }
            case S7Consts.p_i32_PDURequest:
                {
                    Value = PduSizeRequested;
                    break;
                }
            default:
                {
                    Result = S7Consts.errCliInvalidParamNumber;
                    break;
                }
        }
        return Result;
    }

    // Set Properties for compatibility with Snap7.net.cs
    public int SetParam(int ParamNumber, ref int Value)
    {
        var Result = 0;
        switch (ParamNumber)
        {
            case S7Consts.p_u16_RemotePort:
                {
                    PLCPort = Value;
                    break;
                }
            case S7Consts.p_i32_PingTimeout:
                {
                    ConnTimeout = Value;
                    break;
                }
            case S7Consts.p_i32_SendTimeout:
                {
                    SendTimeout = Value;
                    break;
                }
            case S7Consts.p_i32_RecvTimeout:
                {
                    RecvTimeout = Value;
                    break;
                }
            case S7Consts.p_i32_PDURequest:
                {
                    PduSizeRequested = Value;
                    break;
                }
            default:
                {
                    Result = S7Consts.errCliInvalidParamNumber;
                    break;
                }
        }
        return Result;
    }

    #endregion

    #region [Info Functions / Properties]

    public static string ErrorText(int Error)
    {
        return Error switch
        {
            0 => "OK",
            S7Consts.errTCPSocketCreation => "SYS : Error creating the Socket",
            S7Consts.errTCPConnectionTimeout => "TCP : Connection Timeout",
            S7Consts.errTCPConnectionFailed => "TCP : Connection Error",
            S7Consts.errTCPReceiveTimeout => "TCP : Data receive Timeout",
            S7Consts.errTCPDataReceive => "TCP : Error receiving Data",
            S7Consts.errTCPSendTimeout => "TCP : Data send Timeout",
            S7Consts.errTCPDataSend => "TCP : Error sending Data",
            S7Consts.errTCPConnectionReset => "TCP : Connection reset by the Peer",
            S7Consts.errTCPNotConnected => "CLI : Client not connected",
            S7Consts.errTCPUnreachableHost => "TCP : Unreachable host",
            S7Consts.errIsoConnect => "ISO : Connection Error",
            S7Consts.errIsoInvalidPDU => "ISO : Invalid PDU received",
            S7Consts.errIsoInvalidDataSize => "ISO : Invalid Buffer passed to Send/Receive",
            S7Consts.errCliNegotiatingPDU => "CLI : Error in PDU negotiation",
            S7Consts.errCliInvalidParams => "CLI : invalid param(s) supplied",
            S7Consts.errCliJobPending => "CLI : Job pending",
            S7Consts.errCliTooManyItems => "CLI : too may items (>20) in multi read/write",
            S7Consts.errCliInvalidWordLen => "CLI : invalid WordLength",
            S7Consts.errCliPartialDataWritten => "CLI : Partial data written",
            S7Consts.errCliSizeOverPDU => "CPU : total data exceeds the PDU size",
            S7Consts.errCliInvalidPlcAnswer => "CLI : invalid CPU answer",
            S7Consts.errCliAddressOutOfRange => "CPU : Address out of range",
            S7Consts.errCliInvalidTransportSize => "CPU : Invalid Transport size",
            S7Consts.errCliWriteDataSizeMismatch => "CPU : Data size mismatch",
            S7Consts.errCliItemNotAvailable => "CPU : Item not available",
            S7Consts.errCliInvalidValue => "CPU : Invalid value supplied",
            S7Consts.errCliCannotStartPLC => "CPU : Cannot start PLC",
            S7Consts.errCliAlreadyRun => "CPU : PLC already RUN",
            S7Consts.errCliCannotStopPLC => "CPU : Cannot stop PLC",
            S7Consts.errCliCannotCopyRamToRom => "CPU : Cannot copy RAM to ROM",
            S7Consts.errCliCannotCompress => "CPU : Cannot compress",
            S7Consts.errCliAlreadyStop => "CPU : PLC already STOP",
            S7Consts.errCliFunNotAvailable => "CPU : Function not available",
            S7Consts.errCliUploadSequenceFailed => "CPU : Upload sequence failed",
            S7Consts.errCliInvalidDataSizeRecvd => "CLI : Invalid data size received",
            S7Consts.errCliInvalidBlockType => "CLI : Invalid block type",
            S7Consts.errCliInvalidBlockNumber => "CLI : Invalid block number",
            S7Consts.errCliInvalidBlockSize => "CLI : Invalid block size",
            S7Consts.errCliNeedPassword => "CPU : Function not authorized for current protection level",
            S7Consts.errCliInvalidPassword => "CPU : Invalid password",
            S7Consts.errCliAccessDenied => "CPU : Access denied",
            S7Consts.errCliNoPasswordToSetOrClear => "CPU : No password to set or clear",
            S7Consts.errCliJobTimeout => "CLI : Job Timeout",
            S7Consts.errCliFunctionRefused => "CLI : function refused by CPU (Unknown error)",
            S7Consts.errCliPartialDataRead => "CLI : Partial data read",
            S7Consts.errCliBufferTooSmall => "CLI : The buffer supplied is too small to accomplish the operation",
            S7Consts.errCliDestroying => "CLI : Cannot perform (destroying)",
            S7Consts.errCliInvalidParamNumber => "CLI : Invalid Param Number",
            S7Consts.errCliCannotChangeParam => "CLI : Cannot change this param now",
            S7Consts.errCliFunctionNotImplemented => "CLI : Function not implemented",
            S7Consts.errCliFirmwareNotSupported => "CLI : Firmware not supported",
            S7Consts.errCliDeviceNotSupported => "CLI : Device type not supported",
            _ => "CLI : Unknown error (0x" + Convert.ToString(Error, 16) + ")",
        };
        ;
    }

    public int LastError()
    {
        return _LastError;
    }

    public int RequestedPduLength()
    {
        return PduSizeRequested;
    }

    public int NegotiatedPduLength()
    {
        return PduSizeNegotiated;
    }

    public int ExecTime()
    {
        return ExecutionTime;
    }

    public int ExecutionTime { get; private set; }

    public int PduSizeNegotiated { get; }

    public int PduSizeRequested
    {
        get; set
        {
            if (value < MinPduSizeToRequest)
            {
                value = MinPduSizeToRequest;
            }

            if (value > MaxPduSizeToRequest)
            {
                value = MaxPduSizeToRequest;
            }

            field = value;
        }
    } = 480;

    public int PLCPort { get; set; } = ISOTCP;

    public int ConnTimeout { get; set; } = DefaultTimeout;

    public int RecvTimeout { get; set; } = DefaultTimeout;

    public int SendTimeout { get; set; } = DefaultTimeout;

    public bool Connected => (Socket != null) && Socket.Connected;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (m_disposed)
        {
            return;
        }
        if (disposing)
        {
            // 释放托管资源：停止接收线程并释放底层 Socket。
            // 终结器路径(disposing=false)不触碰托管对象——Socket 自带终结器，线程为后台线程。
            Disconnect();
            Socket?.Dispose();
            Socket = null;
        }
        m_disposed = true;
    }
    #endregion
}
