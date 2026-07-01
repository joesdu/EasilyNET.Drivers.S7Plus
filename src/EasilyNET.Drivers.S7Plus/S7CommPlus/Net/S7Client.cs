// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus.S7Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.Net;

// Teilweise basierend auf Snap7 (Sharp7.cs) von Davide Nardella (Lesser GNU GPL v3).
// 纯异步 ISO-on-TCP 传输层：以异步“接收泵 + 发送队列”取代原先的后台线程与同步 socket 调用。
internal sealed class S7Client : IConnectorCallback, IAsyncDisposable
{
    #region [S7 Telegrams]

    // ISO Connection Request telegram (contains also ISO Header and COTP Header)
    private readonly byte[] ISO_CR = [
        0x03, 0x00, 0x00, 0x24,
        0x1f, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
        0xC0, 0x01, 0x0A,
        0xC1, 0x02, 0x01, 0x00,
        0xC2, 0x10,
        // ab hier TSAP ID (String) "SIMATIC-ROOT-HMI"
    ];

    // TPKT + ISO COTP Header (Connection Oriented Transport Protocol)
    private readonly byte[] TPKT_ISO = [ // 7 bytes
        0x03, 0x00,
        0x00, 0x1f,      // Telegram Length (Data Size + 31 or 35)
        0x02, 0xf0, 0x80 // COTP
    ];

    #endregion

    #region S7commPlus

    private volatile bool m_SslActive; // 接收泵线程读取、请求线程写入，volatile 避免弱内存序下读到陈旧值
    private S7TlsConnector? m_sslconn;
    private byte[] m_recvScratch = new byte[8192]; // 接收泵专用：解出的明文暂存缓冲，复用避免每帧分配
    private readonly ILogger log;
    private bool m_disposed;

    // 出站队列：TLS/明文回调把待发送的 ISO 载荷压入，由发送泵异步串行发出（不阻塞调用方/握手）
    private readonly Channel<byte[]> m_sendQueue =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private CancellationTokenSource? m_loopCts;
    private Task? m_receiveLoop;
    private Task? m_sendLoop;

    // OpenSSL/TLS 连接器要求把密文发到 socket：压入出站队列，由发送泵异步发出。
    public void WriteData(byte[] pData, int dataLength)
    {
        var copy = new byte[dataLength];
        Array.Copy(pData, copy, dataLength);
        m_sendQueue.Writer.TryWrite(copy);
    }

    // TLS 连接器报告已解出明文：取出后交付给上层（OnDataReceived）。
    public void OnDataAvailable()
    {
        // 回调发生时 TLS 连接器必然已建立；此判空仅为消除可空告警并防御异常时序
        if (m_sslconn is null)
        {
            return;
        }
        // 复用接收暂存缓冲（仅接收泵单线程触碰）；OnDataReceived 同步拷贝进 MemoryStream，不持有此数组，
        // 故无需每次新分配，也无需再拷一份 readData。
        var bytesRead = m_sslconn.Receive(ref m_recvScratch, m_recvScratch.Length);
        OnDataReceived?.Invoke(m_recvScratch, bytesRead);
    }

    // 启动 TLS：创建 BouncyCastle 连接器并立即发出 ClientHello。
    // 强制 TLS 1.3 + AES-GCM，因为 S7CommPlus on IsoOnTCP 依赖加密后固定（+17 字节）的长度增量来分片。
    public int SslActivate()
    {
        try
        {
            m_sslconn = new S7TlsConnector(this);
            // 先置位，确保服务器握手应答到达时接收泵会路由到 TLS 解密而非明文解析
            m_SslActive = true;
            m_sslconn.ExpectConnect();
        }
        catch (Exception ex)
        {
            m_SslActive = false;
            log.LogDebug("S7Client - SslActivate: error = " + ex.Message);
            return S7Consts.errOpenSSL;
        }
        return 0;
    }

    public _OnDataReceived? OnDataReceived { get; set; }
    public delegate void _OnDataReceived(byte[] PDU, int len);

    #endregion

    #region [Internals]

    private const int ISOTCP = 102; // ISOTCP Port
    private const int DefaultTimeout = 5000;
    private const int IsoHSize = 7; // TPKT+COTP Header Size

    private string IPAddress = string.Empty;
    private byte LocalTSAP_HI;
    private byte LocalTSAP_LO;
    private byte[]? RemoteTSAP_S;
    private byte LastPDUType;
    private readonly byte[] PDU = new byte[2048]; // 接收泵专用缓冲（仅接收循环触碰）
    private MsgSocket? Socket;
    private int _LastError;

    public S7Client(ILogger? logger = null)
    {
        log = logger ?? NullLogger.Instance;
        CreateSocket();
    }

    private void CreateSocket()
    {
        Socket = new MsgSocket
        {
            ConnectTimeout = ConnTimeout,
            ReadTimeout = RecvTimeout,
            WriteTimeout = SendTimeout
        };
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

    public async ValueTask<int> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _LastError = 0;
        if (!Connected)
        {
            await TCPConnectAsync(cancellationToken).ConfigureAwait(false); // Stage 1: TCP
            if (_LastError == 0)
            {
                await ISOConnectAsync(cancellationToken).ConfigureAwait(false); // Stage 2: ISO-on-TCP
                if (_LastError == 0)
                {
                    StartLoops(); // Stage 3: 启动异步接收泵 + 发送泵
                }
            }
        }
        if (_LastError != 0)
        {
            Disconnect();
        }
        return _LastError;
    }

    private async ValueTask TCPConnectAsync(CancellationToken cancellationToken)
    {
        if (_LastError != 0 || Socket is null)
        {
            return;
        }
        _LastError = await Socket.ConnectAsync(IPAddress, PLCPort, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ISOConnectAsync(CancellationToken cancellationToken)
    {
        // RemoteTSAP_S 必须先经 SetConnectionParams 设置；未设置即视为连接错误
        if (RemoteTSAP_S is null || Socket is null)
        {
            _LastError = S7Consts.errIsoConnect;
            return;
        }
        var isocon = new byte[ISO_CR.Length + RemoteTSAP_S.Length];
        ISO_CR[16] = LocalTSAP_HI;
        ISO_CR[17] = LocalTSAP_LO;
        ISO_CR[3] = (byte)(20 + RemoteTSAP_S.Length);
        ISO_CR[4] = (byte)(15 + RemoteTSAP_S.Length);
        ISO_CR[19] = (byte)RemoteTSAP_S.Length;
        Array.Copy(ISO_CR, isocon, 20);
        Array.Copy(RemoteTSAP_S, 0, isocon, 20, RemoteTSAP_S.Length);

        _LastError = await Socket.SendAsync(isocon, isocon.Length, cancellationToken).ConfigureAwait(false);
        if (_LastError != 0)
        {
            return;
        }
        var size = await RecvIsoPacketAsync(cancellationToken).ConfigureAwait(false);
        if (_LastError != 0)
        {
            return;
        }
        if (size == 36)
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

    // 异步发送应用层数据：TLS 激活时交给连接器加密（其 DrainOutput 经 WriteData 入队），
    // 否则把原始 ISO 载荷直接入队。两者最终都由发送泵 SendIsoPacketAsync 包 ISO 头发出。
    public void Send(byte[] Buffer)
    {
        ArgumentNullException.ThrowIfNull(Buffer);
        if (m_SslActive)
        {
            m_sslconn?.Write(Buffer, Buffer.Length);
        }
        else
        {
            var copy = new byte[Buffer.Length];
            Array.Copy(Buffer, copy, Buffer.Length);
            m_sendQueue.Writer.TryWrite(copy);
        }
    }

    private void StartLoops()
    {
        m_loopCts = new CancellationTokenSource();
        var ct = m_loopCts.Token;
        // 直接启动异步收发泵（在首个 await 处让出），无需 Task.Run 包装
        m_sendLoop = SendLoopAsync(ct);
        m_receiveLoop = ReceiveLoopAsync(ct);
    }

    // 发送泵：串行排空出站队列，逐个包 ISO 头异步发出。
    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var payload in m_sendQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (await SendIsoPacketAsync(payload, ct).ConfigureAwait(false) != 0)
                {
                    break; // socket 发送失败：停止发送泵，上层将因等不到响应而重连
                }
            }
        }
        catch (OperationCanceledException) { /* 断开/释放：正常退出 */ }
        catch (Exception ex) { log.LogDebug(ex, "S7Client - SendLoop: error"); }
    }

    private async ValueTask<int> SendIsoPacketAsync(byte[] payload, CancellationToken ct)
    {
        if (Socket is null)
        {
            return S7Consts.errTCPNotConnected;
        }
        var size = payload.Length;
        var buf = new byte[TPKT_ISO.Length + size];
        Array.Copy(TPKT_ISO, 0, buf, 0, TPKT_ISO.Length);
        SetWordAt(buf, 2, (ushort)(size + TPKT_ISO.Length));
        Array.Copy(payload, 0, buf, TPKT_ISO.Length, size);
        return await Socket.SendAsync(buf, buf.Length, ct).ConfigureAwait(false);
    }

    // 接收泵：持续等待并读取完整 ISO 包；TLS 激活时喂入连接器解密，否则直接交付明文。
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var length = await RecvIsoPacketAsync(ct).ConfigureAwait(false);
                if (length > 0)
                {
                    var size = length - TPKT_ISO.Length;
                    var buffer = new byte[size];
                    Array.Copy(PDU, TPKT_ISO.Length, buffer, 0, size);
                    if (m_SslActive)
                    {
                        try
                        {
                            m_sslconn?.ReadCompleted(buffer, size);
                        }
                        catch (Exception ex)
                        {
                            // TLS 握手/解密异常（如 PLC 发回 TLS Alert）。记录后停止接收泵，触发上层重连。
                            log.LogDebug("S7Client - ReceiveLoop: TLS error = " + ex);
                            break;
                        }
                    }
                    else
                    {
                        OnDataReceived?.Invoke(buffer, size);
                    }
                }
                else if (Socket is not { Connected: true })
                {
                    break; // 连接已关闭
                }
                // 否则为读超时（socket 仍在）：继续等待下一帧
            }
        }
        catch (OperationCanceledException) { /* 断开/释放：正常退出 */ }
        catch (Exception ex) { log.LogDebug(ex, "S7Client - ReceiveLoop: error"); }
    }

    private async ValueTask<int> RecvIsoPacketAsync(CancellationToken ct)
    {
        if (Socket is null)
        {
            _LastError = S7Consts.errTCPNotConnected;
            return 0;
        }
        var Done = false;
        var Size = 0;
        _LastError = 0;
        while (_LastError == 0 && !Done)
        {
            // TPKT (4 bytes)：帧首字节用“无读超时”空闲等待，避免请求间隙的忙轮询
            _LastError = await Socket.ReceiveAsync(PDU, 0, 4, applyReadTimeout: false, ct).ConfigureAwait(false);
            if (_LastError == 0)
            {
                Size = GetWordAt(PDU, 2);
                // 校验 TPKT 总长度：必须 >= 头长且不超过接收缓冲，避免畸形/超大帧越界写入固定 PDU 缓冲
                if (Size < IsoHSize || Size > PDU.Length)
                {
                    _LastError = S7Consts.errIsoInvalidPDU;
                    log.LogDebug("S7Client - RecvIsoPacket: invalid TPKT length {Size}, aborting receive loop", Size);
                    throw new InvalidDataException($"Invalid TPKT length: {Size}");
                }
                if (Size == IsoHSize)
                {
                    _LastError = await Socket.ReceiveAsync(PDU, 4, 3, applyReadTimeout: true, ct).ConfigureAwait(false); // skip empty COTP, loop
                }
                else
                {
                    Done = true;
                }
            }
        }
        if (_LastError == 0)
        {
            _LastError = await Socket.ReceiveAsync(PDU, 4, 3, applyReadTimeout: true, ct).ConfigureAwait(false); // 3 COTP bytes
            if (_LastError == 0)
            {
                LastPDUType = PDU[5];
                _LastError = await Socket.ReceiveAsync(PDU, 7, Size - IsoHSize, applyReadTimeout: true, ct).ConfigureAwait(false);
            }
        }
        return _LastError == 0 ? Size : 0;
    }

    private static ushort GetWordAt(byte[] Buffer, int Pos) => (ushort)((Buffer[Pos] << 8) | Buffer[Pos + 1]);

    private static void SetWordAt(byte[] Buffer, int Pos, ushort Value)
    {
        Buffer[Pos] = (byte)(Value >> 8);
        Buffer[Pos + 1] = (byte)(Value & 0x00FF);
    }

    public byte[]? GetOMSExporterSecret() => m_sslconn?.GetOMSExporterSecret();

    // 同步断开（best-effort）：取消收发泵并关闭 socket。用于错误清理路径。
    public int Disconnect()
    {
        m_loopCts?.Cancel();
        m_sendQueue.Writer.TryComplete();
        Socket?.Close();
        return 0;
    }

    public int PduSizeRequested { get; set; } = 480;
    public int PLCPort { get; set; } = ISOTCP;
    public int ConnTimeout { get; set; } = DefaultTimeout;
    public int RecvTimeout { get; set; } = DefaultTimeout;
    public int SendTimeout { get; set; } = DefaultTimeout;

    public bool Connected => Socket is { Connected: true };

    public async ValueTask DisposeAsync()
    {
        if (m_disposed)
        {
            return;
        }
        m_disposed = true;
        m_loopCts?.Cancel();
        m_sendQueue.Writer.TryComplete();
        try
        {
            var pending = new[] { m_receiveLoop, m_sendLoop }.Where(t => t is not null).Cast<Task>().ToArray();
            if (pending.Length > 0)
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
        catch (TimeoutException) { /* 超时后继续关闭 */ }
        catch { /* 关闭期间忽略 */ }
        Socket?.Close();
        Socket = null;
        m_loopCts?.Dispose();
    }
    #endregion
}
