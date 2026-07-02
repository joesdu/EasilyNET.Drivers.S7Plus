// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using EasilyNET.Drivers.S7Plus.S7CommPlus;
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Net;
using System.Text.RegularExpressions;

namespace EasilyNET.Drivers.S7Plus;

internal sealed partial class S7CommPlusConnection : IAsyncDisposable
{
    #region Private Members
    private readonly ILogger log;
    private S7Client m_client = null!;          // 在 ConnectAsync 中创建后整个连接生命周期内有效
    private MemoryStream m_ReceivedPDU = null!;  // 由接收泵在收到完整 PDU 后赋值
    private MemoryStream m_ReceivedTempPDU = null!;
    // 响应/通知分流：响应按 SequenceNumber 认领它的请求，通知单独入队，
    // 从根本上杜绝纯 FIFO 顺序认领造成的“响应错位 → 读/写到别的点位的值”。
    private readonly Queue<(ushort Seq, MemoryStream Pdu)> m_ReceivedResponses = new();
    private readonly Queue<MemoryStream> m_ReceivedNotifications = new();
    // 接收队列的快速同步锁 + 可用信号量（异步等待，取代原 Mutex + Thread.Sleep 忙等待）
    private readonly Lock m_pduLock = new();
    private readonly SemaphoreSlim m_responseSignal = new(0);
    private readonly SemaphoreSlim m_notificationSignal = new(0);
    // 当前在途请求期望的响应序列号；同一连接经上层 gate 串行化，任一时刻至多一个在途请求。
    private ushort m_pendingResponseSeq;
    // 通知队列上限：订阅开启但无人消费时防止无界增长（丢最旧）。
    private const int MaxNotificationQueue = 256;

    private bool m_ReceivedNeedMoreDataForCompletePDU;
    private bool m_NewS7CommPlusReceived;
    private uint m_SessionId;

    public uint SessionId2 { get; private set; }

    private int m_ReadTimeout = 5000;
    private ushort m_SequenceNumber;
    private uint m_IntegrityId;
    private uint m_IntegrityId_Set;
    private readonly CommRessources m_CommRessources = new();

    private List<DatablockInfo>? dbInfoList;
    private readonly List<PObject> typeInfoList = [];

    // 注：原先用可重入 Monitor 把"发送→等待→反序列化"串行化以防轮询/写线程交错。
    // 现由上层 S7PlusClient 的异步信号量串行化全部 I/O（同一连接同一时刻只有一个请求流），
    // 接收泵仅向线程安全的 PDU 队列投递，故此处无需连接级锁。
    #endregion

    #region Public Members
    public int m_LastError;

    #endregion

    #region Private Methods

    private ushort GetNextSequenceNumber()
    {
        if (m_SequenceNumber == ushort.MaxValue)
        {
            m_SequenceNumber = 1;
        }
        else
        {
            m_SequenceNumber++;
        }
        return m_SequenceNumber;
    }

    // We must count the IntegrityId for different functions of the protocol.
    // As a first guess functions for setting variables need separate counters.
    // Use the functioncode to differ between the which sequence/integrity counter values.
    private uint GetNextIntegrityId(ushort functioncode)
    {
        uint ret;
        switch (functioncode)
        {
            case Functioncode.SetMultiVariables:
            case Functioncode.SetVariable:
            case Functioncode.SetVarSubStreamed:
            case Functioncode.DeleteObject:
            case Functioncode.CreateObject:
                if (m_IntegrityId_Set == uint.MaxValue)
                {
                    m_IntegrityId_Set = 0;
                }
                else
                {
                    m_IntegrityId_Set++;
                }
                ret = m_IntegrityId_Set;
                break;
            default:
                if (m_IntegrityId == uint.MaxValue)
                {
                    m_IntegrityId = 0;
                }
                else
                {
                    m_IntegrityId++;
                }
                ret = m_IntegrityId;
                break;
        }
        return ret;
    }

    // 异步等待一个完整响应 PDU：由接收泵在投递 PDU（或检出致命 SystemEvent）时释放信号量，
    // 取代原先的 Thread.Sleep(2) 忙等待。超时或取消视为接收错误。
    private async ValueTask WaitForNewS7plusReceivedAsync(int Timeout, CancellationToken cancellationToken)
    {
        // 在总超时窗口内循环：丢弃序列号不符的陈旧响应（上一请求超时后迟到的回包），
        // 只交付与当前在途请求 m_pendingResponseSeq 匹配的响应，杜绝响应错位。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);
        while (true)
        {
            try
            {
                await m_responseSignal.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                m_LastError = S7Consts.errTCPDataReceive;
                throw; // 调用方主动取消，向上传播
            }
            catch (OperationCanceledException)
            {
                if (log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug("S7CommPlusConnection - WaitForNewS7plusReceived: ERROR: Timeout!");
                }
                if (m_LastError == 0) { m_LastError = S7Consts.errTCPDataReceive; }
                return;
            }
            // 被致命 SystemEvent 唤醒（无 PDU 入队，m_LastError 已置位）
            if (m_LastError != 0) { return; }
            (ushort Seq, MemoryStream Pdu)? item = null;
            lock (m_pduLock)
            {
                if (m_ReceivedResponses.Count > 0) { item = m_ReceivedResponses.Dequeue(); }
            }
            if (item is null) { continue; } // 杂散信号，继续等待
            if (item.Value.Seq == m_pendingResponseSeq)
            {
                m_ReceivedPDU = item.Value.Pdu;
                return;
            }
            // 序列号不符：上一请求超时后迟到的陈旧响应，丢弃并继续等待本次响应
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("S7CommPlusConnection - WaitForNewS7plusReceived: discarding stale response seq={Seq}, expected {PendingResponseSeq}", item.Value.Seq, m_pendingResponseSeq);
            }
            await item.Value.Pdu.DisposeAsync();
        }
    }

    // 等待一个订阅通知 PDU（非请求触发）。与响应分流到独立队列，互不干扰。
    private async ValueTask WaitForNotificationAsync(int Timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);
        while (true)
        {
            try
            {
                await m_notificationSignal.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                m_LastError = S7Consts.errTCPDataReceive;
                throw;
            }
            catch (OperationCanceledException)
            {
                if (m_LastError == 0) { m_LastError = S7Consts.errTCPDataReceive; }
                return;
            }
            if (m_LastError != 0) { return; }
            MemoryStream? pdu = null;
            lock (m_pduLock)
            {
                if (m_ReceivedNotifications.Count > 0) { pdu = m_ReceivedNotifications.Dequeue(); }
            }
            if (pdu is null) { continue; }
            m_ReceivedPDU = pdu;
            return;
        }
    }

    private int SendS7plusFunctionObject(IS7pRequest funcObj)
    {
        // If we don't have a SessionId, this must be the first CreateObjectRequest, where we use the Id for NullServerSession
        funcObj.SessionId = m_SessionId == 0 ? Ids.ObjectNullServerSession : m_SessionId;

        // Insert SequenceNumber and IntegrityId, if neccessary for object type and state of communication
        funcObj.SequenceNumber = GetNextSequenceNumber();
        // 记录本次请求的序列号：响应等待据此认领，丢弃序列号不符的陈旧/迟到响应
        m_pendingResponseSeq = funcObj.SequenceNumber;
        if (funcObj.WithIntegrityId)
        {
            funcObj.IntegrityId = GetNextIntegrityId(funcObj.FunctionCode);
        }

        // 预分配并用 GetBuffer() 直接取底层缓冲（SendS7plusPDUdata 仅按长度即时读取并拷贝出去，不持有），
        // 避免 ToArray() 的整包再拷贝；预设容量减少增长重分配。
        using var stream = new MemoryStream(512);
        funcObj.Serialize(stream);
        return SendS7plusPDUdata(stream.GetBuffer(), (int)stream.Length, funcObj.ProtocolVersion);
    }

    private int SendS7plusPDUdata(byte[] sendPduData, int bytesToSend, byte protoVersion)
    {
        m_LastError = 0;

        int curSize;
        var sourcePos = 0;
        int sendLen;
        var NegotiatedIsoPduSize = 1024;// TODO: Respect the negotiated TPDU size

        // 4 Byte TPKT Header
        // 3 Byte ISO-Header
        // 5 Byte TLS Header + 17 Bytes addition from TLS
        // 4 Byte S7CommPlus Header
        // 4 Byte S7CommPlus Trailer (must fit into last PDU)
        var MaxSize = NegotiatedIsoPduSize - 4 - 3 - 5 - 17 - 4 - 4;
        var packet = new byte[MaxSize + 4]; //max packet size is always MaxSize + PDU Header

        while (bytesToSend > 0)
        {
            if (bytesToSend > MaxSize)
            {
                curSize = MaxSize;
                bytesToSend -= MaxSize;
            }
            else
            {
                curSize = bytesToSend;
                bytesToSend -= curSize;
            }
            // Header
            packet[0] = 0x72;
            packet[1] = protoVersion;
            packet[2] = (byte)(curSize >> 8);
            packet[3] = (byte)(curSize & 0x00FF);
            // Data part
            Array.Copy(sendPduData, sourcePos, packet, 4, curSize);
            sourcePos += curSize;
            sendLen = 4 + curSize;

            // Trailer only in last packet
            if (bytesToSend == 0)
            {
                Array.Resize(ref packet, sendLen + 4); //resize only the last package to sendLen + TrailerLen
                packet[sendLen] = 0x72;
                sendLen++;
                packet[sendLen] = protoVersion;
                sendLen++;
                packet[sendLen] = 0;
                sendLen++;
                packet[sendLen] = 0;
            }
            m_client.Send(packet);
        }
        return m_LastError;
    }

    private void OnDataReceived(byte[] PDU, int len)
    {
        // In this method, we've got always a complete TPDU (from protocol layer above) without fragmentation
        // At this point, we can detect if we receive a fragmented S7CommPlus PDU.
        // If not fragmented, then TPKT.Length - 15 is equal of the length in S7CommPlus.Header.
        // 15 bytes because: 4 Bytes TPKT.Header.len + 3 Bytes ISO.Header.Len + 4 Bytes S7CommPlus.Header.len + 4 Bytes S7CommPlus.trailer.Len.
        // Since the pure userdata of the TPDU comes in here, that is only minus 4 bytes header + 4 bytes trailer.
        // 
        // Special handling for SystemEvents with ProtocolVersion = 0xfe:
        // Here's only a header.
        // Because of this, the first byte for the ProtocolVersion must be written in then stream at first.
        // The datalength must not be written into the stream, because it's not valid on fragmented PDUs
        // for the complete length, only for the single fragment.

        // This method is called from a different thread.
        // If we use subscriptions or alarming, we may get new data before the last PDU was processed completely.
        // First step we push the complete PDU to a queue.
        // TODO: m_LastError handling would also not work as expected. This needs some more redesign.

        if (!m_ReceivedNeedMoreDataForCompletePDU)
        {
            m_ReceivedTempPDU = new MemoryStream();
        }
        // S7comm-plus
        byte protoVersion;
        var pos = 0;
        // Check header
        if (PDU[pos] != 0x72)
        {
            m_ReceivedNeedMoreDataForCompletePDU = false;
            m_LastError = S7Consts.errIsoInvalidPDU;
            return;
        }
        pos++;
        protoVersion = PDU[pos];
        if (protoVersion is not ProtocolVersion.V1 and not ProtocolVersion.V2 and not ProtocolVersion.V3 and not ProtocolVersion.SystemEvent)
        {
            m_ReceivedNeedMoreDataForCompletePDU = false;
            m_LastError = S7Consts.errIsoInvalidPDU;
            return;
        }
        // For the first fragment, write the ProtocolVersion into the stream in advance
        if (!m_ReceivedNeedMoreDataForCompletePDU)
        {
            m_ReceivedTempPDU.Write(PDU, pos, 1);
        }
        pos++;

        // Read the length of the data-part from header
        var s7HeaderDataLen = GetWordAt(PDU, pos);
        pos += 2;
        if (s7HeaderDataLen > 0)
        {
            // Special handling for SystemEvent 0xfe PDUs:
            // This only confirms a few data, but also reports major protocol errors (e.g. incorrect sequence numbers).
            // The confirms can be discarded (for now), but the errors are relevant, because a connection termination is neccessary.
            // As we don't have a trailer on this types, it's not possible that they are transmitted as fragments.
            if (protoVersion == ProtocolVersion.SystemEvent)
            {
                if (log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug("S7CommPlusConnection - OnDataReceived: ProtocolVersion 0xfe SystemEvent received");
                }
                m_ReceivedTempPDU.Write(PDU, pos, s7HeaderDataLen);
                // Create SystemEventObject
                m_ReceivedNeedMoreDataForCompletePDU = false;
                m_ReceivedTempPDU.Position = 0;
                m_NewS7CommPlusReceived = false;

                var sysevt = SystemEvent.DeserializeFromPdu(m_ReceivedTempPDU);
                if (sysevt?.IsFatalError() == true)
                {
                    if (log.IsEnabled(LogLevel.Debug))
                    {
                        log.LogDebug("S7CommPlusConnection - OnDataReceived: SystemEvent has fatal error");
                    }
                    // Termination neccessary：置错误并唤醒等待者（无 PDU 入队）。
                    // 释放两个信号量：无论当前等待的是响应还是通知都能立即返回错误（连接随后会被丢弃重连）。
                    m_LastError = S7Consts.errIsoInvalidPDU;
                    m_responseSignal.Release();
                    m_notificationSignal.Release();
                }
                else
                {
                    if (log.IsEnabled(LogLevel.Debug))
                    {
                        log.LogDebug("S7CommPlusConnection - OnDataReceived: SystemEvent with non fatal error, do nothing");
                    }
                }
            }
            else
            {
                // Copy data part to destination stream
                m_ReceivedTempPDU.Write(PDU, pos, s7HeaderDataLen);
                // If this is a fragmented PDU, then at this point no trailer
                if ((len - 4 - 4) == s7HeaderDataLen)
                {
                    m_ReceivedNeedMoreDataForCompletePDU = false;
                    m_ReceivedTempPDU.Position = 0;    // Set position back to zero, ready for readout
                    m_NewS7CommPlusReceived = true;
                }
                else
                {
                    m_ReceivedNeedMoreDataForCompletePDU = true;
                }
            }
        }

        // If a complete (usable) PDU is received, route it by opcode (threadsafe) for readout.
        if (m_NewS7CommPlusReceived)
        {
            // 队列内 PDU 流布局：[0]=ProtocolVersion, [1]=Opcode, [2-3]=保留, [4-5]=功能码, [6-7]=保留, [8-9]=SequenceNumber
            var streamBuf = m_ReceivedTempPDU.GetBuffer();
            var pduLen = m_ReceivedTempPDU.Length;
            var opcode = pduLen > 1 ? streamBuf[1] : (byte)0;
            if (opcode == Opcode.Notification)
            {
                lock (m_pduLock)
                {
                    while (m_ReceivedNotifications.Count >= MaxNotificationQueue)
                    {
                        m_ReceivedNotifications.Dequeue().Dispose(); // 丢最旧，防止无人消费时无界增长
                    }
                    m_ReceivedNotifications.Enqueue(m_ReceivedTempPDU);
                }
                m_notificationSignal.Release();
            }
            else
            {
                // 响应：取出 SequenceNumber 供等待方按需认领（认不上的陈旧响应会被丢弃）
                var seq = pduLen >= 10 ? (ushort)((streamBuf[8] << 8) | streamBuf[9]) : (ushort)0;
                lock (m_pduLock)
                {
                    m_ReceivedResponses.Enqueue((seq, m_ReceivedTempPDU));
                }
                m_responseSignal.Release();
            }
            m_NewS7CommPlusReceived = false;
        }
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

    private void PrintBuf(byte[] b)
    {
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("Received bytes: {Bytes}", string.Join(" ", b.Select(x => "0x" + x.ToString("X02", CultureInfo.InvariantCulture))));
        }
    }

    private int CheckResponseWithIntegrity(IS7pRequest request, IS7pResponse? response)
    {
        if (response == null)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("checkResponseWithIntegrity: ERROR! response == null");
            }
            return S7Consts.errIsoInvalidPDU;
        }
        if (request.SequenceNumber != response.SequenceNumber)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("checkResponseWithIntegrity: ERROR! SeqenceNumber of Response ({ResponseSequenceNumber}) doesn't match Request ({RequestSequenceNumber})", response.SequenceNumber, request.SequenceNumber);
            }
            return S7Consts.errIsoInvalidPDU;
        }
        // Overflow is possible and allowed
        var reqIntegCheck = request.SequenceNumber + request.IntegrityId;
        if (response.IntegrityId != reqIntegCheck)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("checkResponseWithIntegrity: ERROR! IntegrityId of the Response ({ResponseIntegrityId}) doesn't match Request ({RequestIntegrityId})", response.IntegrityId, reqIntegCheck);
            }
            // Don't return this as error so far
        }
        return 0;
    }
    #endregion

    #region Public Methods

    public S7CommPlusConnection(ILogger? logger = null)
    {
        log = logger ?? NullLogger.Instance;
        S7Log.Instance = log;
    }

    /// <summary>
    /// Establishes a connection to the PLC.
    /// </summary>
    /// <param name="address">PLC IP address</param>
    /// <param name="password">PLC password (if set)</param>
    /// <param name="username">PLC username (leave empty for legacy login)</param>
    /// <param name="timeoutMs">read timeout in milliseconds (default: 5000 ms)</param>
    /// <param name="ct">cancellation token</param>
    /// <returns></returns>
    public async Task<int> ConnectAsync(string address, string password = "", string username = "", int timeoutMs = 5000, CancellationToken ct = default)
    {
        if (timeoutMs > 0)
        {
            m_ReadTimeout = timeoutMs;
        }

        m_LastError = 0;
        int res;
        var Elapsed = Environment.TickCount;
        m_client = new S7Client(log)
        {
            OnDataReceived = this.OnDataReceived
        };
        // 将配置的超时同时应用到 TCP 连接与 socket 收发，避免错误 IP 时固定等待 5s 默认值
        if (timeoutMs > 0)
        {
            m_client.ConnTimeout = timeoutMs;
            m_client.RecvTimeout = timeoutMs;
            m_client.SendTimeout = timeoutMs;
        }

        m_client.SetConnectionParams(address, 0x0600, Encoding.ASCII.GetBytes("SIMATIC-ROOT-HMI"));
        res = await m_client.ConnectAsync(ct).ConfigureAwait(false);
        if (res != 0)
        {
            return res;
        }

        #region Step 1: Unencrypted InitSSL Request / Response

        var sslReq = new InitSslRequest(ProtocolVersion.V1, 0, 0);
        res = SendS7plusFunctionObject(sslReq);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct);
        if (m_LastError != 0)
        {
            m_client.Disconnect();
            return m_LastError;
        }
        var sslRes = InitSslResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (sslRes == null)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("S7CommPlusConnection - Connect: InitSslResponse with Error!");
            }
            m_client.Disconnect();
            return m_LastError;
        }
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("S7CommPlusConnection - Connect: Step1 InitSSL OK (plaintext). Activating TLS...");
        }

        #endregion

        #region Step 2: Activate TLS. Everything from here onwards is TLS encrypted.

        res = m_client.SslActivate();
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("S7CommPlusConnection - Connect: Step2 TLS activated, ClientHello sent. Sending CreateObjectRequest...");
        }

        #endregion

        #region Step 3: CreateObjectRequest / Response (with TLS)

        var createObjReq = new CreateObjectRequest(ProtocolVersion.V1, 0, false);
        createObjReq.SetNullServerSessionData();
        res = SendS7plusFunctionObject(createObjReq);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct);
        if (m_LastError != 0)
        {
            m_client.Disconnect();
            return m_LastError;
        }

        var createObjRes = CreateObjectResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (createObjRes == null)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("S7CommPlusConnection - Connect: CreateObjectResponse with Error!");
            }
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }
        // There are (always?) at least two IDs in the response.
        // Usually the first is used for polling data, and the 2nd for jobs which use notifications, e.g. alarming, subscriptions.
        m_SessionId = createObjRes.ObjectIds![0];
        SessionId2 = createObjRes.ObjectIds[1];
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("S7CommPlusConnection - Connect: Using SessionId=0x{SessionId:X04}", m_SessionId);
        }

        // Evaluate Struct 314
        var sval = createObjRes.ResponseObject!.GetAttribute(Ids.ServerSessionVersion);
        var serverSession = (ValueStruct)sval;

        #endregion

        #region Step 4: SetMultiVariablesRequest / Response

        var setMultiVarReq = new SetMultiVariablesRequest(ProtocolVersion.V2);
        setMultiVarReq.SetSessionSetupData(m_SessionId, serverSession);
        res = SendS7plusFunctionObject(setMultiVarReq);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct);
        if (m_LastError != 0)
        {
            m_client.Disconnect();
            return m_LastError;
        }

        var setMultiVarRes = SetMultiVariablesResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (setMultiVarRes == null)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("S7CommPlusConnection - Connect: SetMultiVariablesResponse with Error!");
            }
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }

        #endregion

        #region Step 5: Read SystemLimits
        res = await m_CommRessources.ReadMaxAsync(this, ct).ConfigureAwait(false);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        #endregion

        #region Step 6: Password
        res = await LegitimateAsync(serverSession, password, username, ct).ConfigureAwait(false);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        #endregion

        // If everything has been error-free up to this point, then the connection has been established successfully.
        if (log.IsEnabled(LogLevel.Debug))
        {
            log.LogDebug("S7CommPlusConnection - Connect: Time for connection establishment: {ElapsedMilliseconds} ms.", Environment.TickCount - Elapsed);
        }
        return 0;
    }

    /// <summary>
    /// 优雅断开：删除会话对象后关闭底层连接。
    /// </summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        try
        {
            await DeleteObjectAsync(m_SessionId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* 取消：直接关闭 */ }
        catch (Exception ex)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug(ex, "S7CommPlusConnection - Disconnect: DeleteObject error");
            }
        }
        m_client.Disconnect();
    }

    /// <summary>
    /// Deletes the object with the given Id.
    /// </summary>
    /// <param name="deleteObjectId">The object Id to delete</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>0 on success</returns>
    private async Task<int> DeleteObjectAsync(uint deleteObjectId, CancellationToken ct = default)
    {
        int res;
        var delObjReq = new DeleteObjectRequest(ProtocolVersion.V2)
        {
            DeleteObjectId = deleteObjectId
        };
        res = SendS7plusFunctionObject(delObjReq);
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            return m_LastError;
        }
        // If we delete our own session id, then there's no IntegrityId in the response.
        // And the error code gives an error, but not a fatal one.
        // If we delete another object, there should be an IntegrityId in the response, and
        // the response gives no error.
        if (deleteObjectId == m_SessionId)
        {
            DeleteObjectResponse.DeserializeFromPdu(m_ReceivedPDU, false);
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("S7CommPlusConnection - DeleteSession: Deleted our own Session Id object, not checking the response.");
            }
            m_SessionId = 0; // not valid anymore
            SessionId2 = 0;
        }
        else
        {
            var delObjRes = DeleteObjectResponse.DeserializeFromPdu(m_ReceivedPDU, true);
            res = CheckResponseWithIntegrity(delObjReq, delObjRes);
            if (res != 0)
            {
                return res;
            }
            if (delObjRes!.ReturnValue != 0)
            {
                if (log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug("S7CommPlusConnection - DeleteSession: Executed with Error! ReturnValue={ReturnValue}", delObjRes.ReturnValue);
                }
                res = -1;
            }
        }
        return res;
    }

    public async Task<(int res, List<object?> values, List<ulong> errors)> ReadValuesAsync(List<ItemAddress> addresslist, CancellationToken ct = default)
    {
        // The requester must pass the internal type with the request, otherwise not all return values can be converted automatically.
        // For example, strings are transmitted as UInt-Array.
        var values = new List<object?>();
        var errors = new List<ulong>();
        // Initialize error fields to error value
        for (var i = 0; i < addresslist.Count; i++)
        {
            values.Add(null);
            errors.Add(0xffffffffffffffff);
        }

        // Split request into chunks, taking the MaxTags per request into account
        var chunk_startIndex = 0;
        do
        {
            var getMultiVarReq = new GetMultiVariablesRequest(ProtocolVersion.V2);

            getMultiVarReq.AddressList.Clear();
            var count_perChunk = 0;
            while (count_perChunk < m_CommRessources.TagsPerReadRequestMax && (chunk_startIndex + count_perChunk) < addresslist.Count)
            {
                getMultiVarReq.AddressList.Add(addresslist[chunk_startIndex + count_perChunk]);
                count_perChunk++;
            }

            var res = SendS7plusFunctionObject(getMultiVarReq);
            m_LastError = 0;
            await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
            if (m_LastError != 0)
            {
                return (m_LastError, values, errors);
            }
            var getMultiVarRes = GetMultiVariablesResponse.DeserializeFromPdu(m_ReceivedPDU);
            res = CheckResponseWithIntegrity(getMultiVarReq, getMultiVarRes);
            if (res != 0)
            {
                return (res, values, errors);
            }
            // ReturnValue shows also an error, if only one single variable could not be read
            if (getMultiVarRes!.ReturnValue != 0)
            {
                if (log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug("S7CommPlusConnection - ReadValues: Executed with Error! ReturnValue={ReturnValue}", getMultiVarRes.ReturnValue);
                }
            }

            // TODO: If a variable could not be read, there is no value, but there is an ErrorValue.
            // The user must therefore check whether Value != null. Maybe there's a more elegant solution.
            foreach (var v in getMultiVarRes.Values)
            {
                values[chunk_startIndex + (int)v.Key - 1] = v.Value;
                // Initialize error to 0, will be overwritten below if there was an error on an item.
                errors[chunk_startIndex + (int)v.Key - 1] = 0;
            }

            foreach (var ev in getMultiVarRes.ErrorValues)
            {
                errors[chunk_startIndex + (int)ev.Key - 1] = ev.Value;
            }
            chunk_startIndex += count_perChunk;

        } while (chunk_startIndex < addresslist.Count);

        return (m_LastError, values, errors);
    }

    public async Task<(int res, List<ulong> errors)> WriteValuesAsync(List<ItemAddress> addresslist, List<PValue> values, CancellationToken ct = default)
    {
        int res;
        var errors = new List<ulong>();
        for (var i = 0; i < addresslist.Count; i++)
        {
            // Initialize to no error value, as there's no explicit value for write success.
            errors.Add(0);
        }

        // Split request into chunks, taking the MaxTags per request into account
        var chunk_startIndex = 0;
        do
        {
            var setMultiVarReq = new SetMultiVariablesRequest(ProtocolVersion.V2);
            setMultiVarReq.AddressListVar.Clear();
            setMultiVarReq.ValueList.Clear();
            var count_perChunk = 0;
            while (count_perChunk < m_CommRessources.TagsPerWriteRequestMax && (chunk_startIndex + count_perChunk) < addresslist.Count)
            {
                setMultiVarReq.AddressListVar.Add(addresslist[chunk_startIndex + count_perChunk]);
                setMultiVarReq.ValueList.Add(values[chunk_startIndex + count_perChunk]);
                count_perChunk++;
            }

            res = SendS7plusFunctionObject(setMultiVarReq);
            if (res != 0)
            {
                return (res, errors);
            }
            m_LastError = 0;
            await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
            if (m_LastError != 0)
            {
                return (m_LastError, errors);
            }

            var setMultiVarRes = SetMultiVariablesResponse.DeserializeFromPdu(m_ReceivedPDU);
            res = CheckResponseWithIntegrity(setMultiVarReq, setMultiVarRes);
            if (res != 0)
            {
                return (res, errors);
            }
            // ReturnValue shows also an error, if only one single variable could not be written
            if (setMultiVarRes!.ReturnValue != 0)
            {
                if (log.IsEnabled(LogLevel.Debug))
                {
                    log.LogDebug("S7CommPlusConnection - WriteValues: Write with errors. ReturnValue={ReturnValue}", setMultiVarRes.ReturnValue);
                }
            }

            foreach (var ev in setMultiVarRes.ErrorValues)
            {
                errors[chunk_startIndex + (int)ev.Key - 1] = ev.Value;
            }
            chunk_startIndex += count_perChunk;

        } while (chunk_startIndex < addresslist.Count);

        return (m_LastError, errors);
    }

    public async Task<int> SetPlcOperatingStateAsync(int state, CancellationToken ct = default)
    {
        int res;
        var setVarReq = new SetVariableRequest(ProtocolVersion.V2)
        {
            InObjectId = Ids.NativeObjects_theCPUexecUnit_Rid,
            Address = Ids.CPUexecUnit_operatingStateReq,
            Value = new ValueDInt(state)
        };

        res = SendS7plusFunctionObject(setVarReq);
        if (res != 0)
        {
            m_client.Disconnect();
            return res;
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            m_client.Disconnect();
            return m_LastError;
        }

        var setVarRes = SetVariableResponse.DeserializeFromPdu(m_ReceivedPDU);
        if (setVarRes == null)
        {
            if (log.IsEnabled(LogLevel.Debug))
            {
                log.LogDebug("S7CommPlusConnection - Connect: SetVariableResponse with Error!");
            }
            m_client.Disconnect();
            return S7Consts.errIsoInvalidPDU;
        }

        return 0;
    }

    public async Task<(int res, List<VarInfo> varInfoList)> BrowseAsync(CancellationToken ct = default)
    {
        int res;
        var varInfoList = new List<VarInfo>();
        var vars = new Browser();
        ExploreRequest exploreReq;
        ExploreResponse? exploreRes;

        #region Read all objects

        var exploreData = new List<BrowseData>();

        exploreReq = new ExploreRequest(ProtocolVersion.V2)
        {
            ExploreId = Ids.NativeObjects_thePLCProgram_Rid,
            ExploreRequestId = Ids.None,
            ExploreChildsRecursive = 1,
            ExploreParents = 0
        };

        // We want to know the following attributes
        exploreReq.AddressList.Add(Ids.ObjectVariableTypeName);
        exploreReq.AddressList.Add(Ids.Block_BlockNumber);
        exploreReq.AddressList.Add(Ids.ASObjectES_Comment);

        res = SendS7plusFunctionObject(exploreReq);
        if (res != 0)
        {
            return (res, varInfoList);
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            return (m_LastError, varInfoList);
        }

        exploreRes = ExploreResponse.DeserializeFromPdu(m_ReceivedPDU, true);
        res = CheckResponseWithIntegrity(exploreReq, exploreRes);
        if (res != 0)
        {
            return (res, varInfoList);
        }

        #endregion

        #region Evaluate all data blocks that then need to be browsed

        var obj = exploreRes!.Objects.First(o => o.ClassId == Ids.PLCProgram_Class_Rid);

        foreach (var ob in obj.GetObjects())
        {
            switch (ob.ClassId)
            {
                case Ids.DB_Class_Rid:
                    var relid = ob.RelationId;
                    var area = relid >> 16;
                    var num = relid & 0xffff;
                    if (area == 0x8a0e)
                    {
                        var name = (ValueWString)ob.GetAttribute(Ids.ObjectVariableTypeName);
                        var data = new BrowseData
                        {
                            DbBlockRelid = relid,
                            DbName = name.Value,
                            DbNumber = num
                        };
                        exploreData.Add(data);
                    }
                    break;
            }
        }

        #endregion

        #region Determine the TypeInfo RID to the RelId from the first response
        // By querying LID = 1 from all DBs you get the RID back with which the type information can be queried.
        // This is neccessary because, for example, with instance DBs (e.g. TON), the type information must
        // not be accessed via the RID of the DB, but of the RID of the TON.
        var readlist = new List<ItemAddress>();
        List<object?> values;
        List<ulong> errors;

        foreach (var data in exploreData)
        {
            if (data.DbNumber > 0) // only process datablocks here, no marker, timer etc.
            {
                // Insert the variable address
                var adr1 = new ItemAddress
                {
                    AccessArea = data.DbBlockRelid,
                    AccessSubArea = Ids.DB_ValueActual
                };
                adr1.LID.Add(1);
                readlist.Add(adr1);
            }
        }
        (res, values, errors) = await ReadValuesAsync(readlist, ct).ConfigureAwait(false);
        if (res != 0)
        {
            return (res, varInfoList);
        }
        #endregion

        #region Pass the preliminary information for recombination to ExploreSymbols

        // Add the response information to the list
        for (var i = 0; i < values.Count; i++)
        {
            if (errors[i] == 0)
            {
                var rid = (ValueRID)values[i]!;
                var data = exploreData[i];
                data.DbBlockTiRelid = rid.Value;
                exploreData[i] = data;
            }
            else
            {
                // On error, set the relid to zero, will be removed from the list in the next step.
                // TODO: Report this as an error?
                var data = exploreData[i];
                data.DbBlockTiRelid = 0;
                exploreData[i] = data;
            }
        }
        // Remove elements with DbBlockTiRelid == 0. This occurs e.g. on datablocks only present in load memory.
        // The informations can't be used any further (at least not for variable access).
        exploreData.RemoveAll(item => item.DbBlockTiRelid == 0);

        foreach (var ed in exploreData)
        {
            vars.AddBlockNode(ENodeType.Root, ed.DbName, ed.DbBlockRelid, ed.DbBlockTiRelid);
        }

        // Add IQMCT areas manually
        vars.AddBlockNode(ENodeType.Root, "IArea", Ids.NativeObjects_theIArea_Rid, 0x90010000);
        vars.AddBlockNode(ENodeType.Root, "QArea", Ids.NativeObjects_theQArea_Rid, 0x90020000);
        vars.AddBlockNode(ENodeType.Root, "MArea", Ids.NativeObjects_theMArea_Rid, 0x90030000);
        vars.AddBlockNode(ENodeType.Root, "S7Timers", Ids.NativeObjects_theS7Timers_Rid, 0x90050000);
        vars.AddBlockNode(ENodeType.Root, "S7Counters", Ids.NativeObjects_theS7Counters_Rid, 0x90060000);

        #endregion

        #region Read the Type Info Container (as a single big PDU, must be proven to be the way to go in big programs)
        exploreReq = new ExploreRequest(ProtocolVersion.V2)
        {
            // With ObjectOMSTypeInfoContainer we get all in a big PDU (with maybe hundreds of fragments)
            ExploreId = Ids.ObjectOMSTypeInfoContainer,
            ExploreRequestId = Ids.None,
            ExploreChildsRecursive = 1,
            ExploreParents = 0
        };

        res = SendS7plusFunctionObject(exploreReq);
        if (res != 0)
        {
            return (res, varInfoList);
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            return (m_LastError, varInfoList);
        }
        #endregion

        #region Process the response, and build the complete variables list
        exploreRes = ExploreResponse.DeserializeFromPdu(m_ReceivedPDU, true);
        res = CheckResponseWithIntegrity(exploreReq, exploreRes);
        if (res != 0)
        {
            return (res, varInfoList);
        }
        var objs = exploreRes!.Objects.First(o => o.ClassId == Ids.ClassOMSTypeInfoContainer);

        vars.SetTypeInfoContainerObjects(objs.GetObjects());
        vars.BuildTree();
        vars.BuildFlatList();
        varInfoList = vars.VarInfoList;
        #endregion

        return (0, varInfoList);
    }

    // 符号游标：替代异步方法中无法使用的 ref string，按引用语义在各级解析间传递与消费符号字符串。
    private sealed class SymbolRef(string value)
    {
        public string Value = value;
    }

    /// <summary>
    /// Gets the first level of a tag symbol string. Removes the " used to escape special chars.
    /// </summary>
    /// <param name="symbolRef">plc tag symbol</param>
    /// <returns>The first level of the symbol string</returns>
    /// <exception cref="Exception">Symbol syntax error</exception>
    private static string ParseSymbolLevel(SymbolRef symbolRef)
    {
        var symbol = symbolRef.Value;
        try
        {
            if (symbol.StartsWith('"'))
            {
                var idx = symbol.IndexOf('"', 1);
                if (idx < 0)
                {
                    throw new Exception("Symbol syntax error");
                }

                var lvl = symbol[1..idx];
                symbol = symbol[(idx + 1)..];
                if (symbol.StartsWith('.'))
                {
                    symbol = symbol[1..];
                }

                return lvl;
            }
            else
            {
                var idx = symbol.IndexOf('.');
                var idx2 = symbol.IndexOf('[', 1);
                if (idx2 >= 0 && (idx2 < idx || idx < 0))
                {
                    idx = idx2;
                }

                if (idx >= 0)
                {
                    var lvl = symbol[..idx];
                    symbol = symbol[idx..];
                    if (symbol.StartsWith('.'))
                    {
                        symbol = symbol[1..];
                    }

                    return lvl;
                }
                else
                {
                    var lvl = symbol;
                    symbol = "";
                    return lvl;
                }
            }
        }
        finally
        {
            symbolRef.Value = symbol;
        }
    }

    /// <summary>
    /// Gets the typeinfo by given ti relid from the internal buffer. If it's not found in the buffer
    /// it's fetched from the PLC and stored in the buffer.
    /// </summary>
    /// <param name="ti_relid">type info relid</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>type info</returns>
    /// <exception cref="Exception">Could not get type info</exception>
    internal async Task<PObject?> GetTypeInfoByRelIdAsync(uint ti_relid, CancellationToken ct = default)
    {
        var pObj = typeInfoList.Find(ti => ti.RelationId == ti_relid);
        if (pObj == null)
        {
            // Type info not found in list, request it from plc
            var (res, newPObj) = await GetTypeInformationAsync(ti_relid, ct).ConfigureAwait(false);
            if (res != 0)
            {
                throw new Exception("Could not get type info");
            }

            typeInfoList.AddRange(newPObj);
            // Try again
            pObj = typeInfoList.Find(ti => ti.RelationId == ti_relid);
        }
        return pObj;
    }

    /// <summary>
    /// Calculates the access sequence for 1 dimensional arrays.
    /// </summary>
    /// <param name="symbol">plc tag symbol</param>
    /// <param name="varType">Var type that holds the dim info</param>
    /// <param name="varInfo">used to build access sequence</param>
    /// <exception cref="Exception">Symbol syntax error</exception>
    private static void CalcAccessSeqFor1DimArray(SymbolRef symbol, PVartypeListElement varType, VarInfo varInfo)
    {
        var m = SingleDimensionIndexRegex.Match(symbol.Value);
        if (!m.Success)
        {
            throw new Exception("Symbol syntax error");
        }

        ParseSymbolLevel(symbol); // remove index from symbol string
        var arrayIndex = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);

        var ioit = varType.OffsetInfoType as IOffsetInfoType_1Dim;
        var arrayElementCount = ioit?.GetArrayElementCount();
        var arrayLowerBounds = ioit?.GetArrayLowerBounds();

        if (arrayIndex - arrayLowerBounds >= arrayElementCount)
        {
            throw new Exception("Out of bounds");
        }

        if (arrayIndex < arrayLowerBounds)
        {
            throw new Exception("Out of bounds");
        }

        varInfo.AccessSequence += $".{arrayIndex - arrayLowerBounds:X}";
        if (varType.OffsetInfoType!.HasRelation())
        {
            varInfo.AccessSequence += ".1"; // additional ".1" for array of struct
        }
    }

    /// <summary>
    /// Calculates the access sequence for multi-dimensional arrays.
    /// </summary>
    /// <param name="symbol">plc tag symbol</param>
    /// <param name="varType">Var type that holds the dim info</param>
    /// <param name="varInfo">used to build access sequence</param>
    /// <exception cref="Exception">Symbol syntax error</exception>
    private static void CalcAccessSeqForMDimArray(SymbolRef symbol, PVartypeListElement varType, VarInfo varInfo)
    {
        var m = MultiDimensionIndexRegex.Match(symbol.Value);
        if (!m.Success)
        {
            throw new Exception("Symbol syntax error");
        }

        ParseSymbolLevel(symbol); // remove index from symbol string
        var idxs = m.Groups[1].Value.Replace(" ", "", StringComparison.InvariantCulture);

        var indexes = Array.ConvertAll(idxs.Split(','), e => int.Parse(e, CultureInfo.InvariantCulture));
        var ioit = (IOffsetInfoType_MDim)varType.OffsetInfoType!;
        var MdimArrayElementCount = (uint[])ioit.GetMdimArrayElementCount().Clone();
        var MdimArrayLowerBounds = ioit.GetMdimArrayLowerBounds();

        // check dim count
        var dimCount = MdimArrayElementCount.Aggregate(0, (acc, act) => acc += (act > 0) ? 1 : 0);
        if (dimCount != indexes.Length)
        {
            throw new Exception("Out of bounds");
        }
        // check bounds
        for (var i = 0; i < dimCount; ++i)
        {
            indexes[i] = indexes[i] - MdimArrayLowerBounds[dimCount - i - 1];
            if (indexes[i] >= MdimArrayElementCount[dimCount - i - 1])
            {
                throw new Exception("Out of bounds");
            }

            if (indexes[i] < 0)
            {
                throw new Exception("Out of bounds");
            }
        }

        // calc dim size
        if (varType.Softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL && MdimArrayElementCount[0] % 8 != 0)
        {
            // 仅当未对齐到 8 时才补齐；否则会多加一个 8（与 Browser.AddFlatSubnodes 的判定保持一致）
            MdimArrayElementCount[0] += 8 - (MdimArrayElementCount[0] % 8); // for bool must be a mutiple of 8!
        }
        var dimSize = new uint[dimCount];
        uint g = 1;
        for (var i = 0; i < dimCount - 1; ++i)
        {
            dimSize[i] = g;
            g *= MdimArrayElementCount[i];
        }
        dimSize[dimCount - 1] = g;

        // calc id
        var arrayIndex = 0;
        for (var i = 0; i < dimCount; ++i)
        {
            arrayIndex += indexes[i] * (int)dimSize[dimCount - i - 1];
        }

        varInfo.AccessSequence += $".{arrayIndex:X}";
        if (varType.OffsetInfoType!.HasRelation())
        {
            varInfo.AccessSequence += ".1"; // additional ".1" for array of struct
        }
    }

    /// <summary>
    /// Browses the symbol level by level recursively. Fetches missing type info automatically from the plc.
    /// </summary>
    /// <param name="ti_relid">type info relid</param>
    /// <param name="symbol">plc tag symbol</param>
    /// <param name="varInfo">used to build access sequence</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>plc tag or null if not found</returns>
    /// <exception cref="Exception">Symbol syntax error, Out of bounds</exception>
    private async Task<PlcTag?> BrowsePlcTagBySymbolAsync(uint ti_relid, SymbolRef symbol, VarInfo varInfo, CancellationToken ct = default)
    {
        var pObj = await GetTypeInfoByRelIdAsync(ti_relid, ct).ConfigureAwait(false) ?? throw new Exception("Could not get type info");
        var levelName = ParseSymbolLevel(symbol);
        // find level name of symbol in var list
        var idx = pObj.VarnameList?.Names?.IndexOf(levelName) ?? -1;
        if (idx < 0)
        {
            return null;
        }

        var varType = pObj.VartypeList!.Elements[idx];
        varInfo.AccessSequence += $".{varType.LID:X}";
        var is1Dim = false;
        if (varType.OffsetInfoType!.Is1Dim())
        {
            if (string.IsNullOrEmpty(symbol.Value))
            {
                is1Dim = true;
            }
            else
            {
                CalcAccessSeqFor1DimArray(symbol, varType, varInfo);
            }
        }
        if (varType.OffsetInfoType.IsMDim())
        {
            CalcAccessSeqForMDimArray(symbol, varType, varInfo);
        }
        if (varType.OffsetInfoType!.HasRelation())
        {
            if (symbol.Value.Length <= 0 && varType.Softdatatype == Softdatatype.S7COMMP_SOFTDATATYPE_DTL)
            {
                return PlcTags.TagFactory(varInfo.Name, new ItemAddress(varInfo.AccessSequence), varType.Softdatatype, is1Dim, log);
            }
            if (symbol.Value.Length <= 0)
            {
                return null;
            }
            else
            {
                var ioit = (IOffsetInfoType_Relation)varType.OffsetInfoType;
                return await BrowsePlcTagBySymbolAsync(ioit.GetRelationId(), symbol, varInfo, ct).ConfigureAwait(false);
            }
        }
        else
        {
            // 字符串类型带上 PLC 声明的真实最大长度，供写入时正确构造 [maxLen][actLen] 头部
            var stringMaxLength = varType.OffsetInfoType is POffsetInfoType_String strOi ? strOi.UnspecifiedOffsetinfo1 : 0;
            return PlcTags.TagFactory(varInfo.Name, new ItemAddress(varInfo.AccessSequence), varType.Softdatatype, is1Dim, log, stringMaxLength);
        }
    }

    /// <summary>
    /// Get the plc tag for the given plc tag symbol. 
    /// </summary>
    /// <param name="symbol">plc tag symbol</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>plc tag, returns null if plc tag could not be found</returns>
    public async Task<PlcTag?> GetPlcTagBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        var varInfo = new VarInfo
        {
            Name = symbol
        };
        // make sure we have the db list
        if (dbInfoList == null)
        {
            int r;
            (r, dbInfoList) = await GetListOfDatablocksAsync(ct).ConfigureAwait(false);
            if (r != 0) { return null; }
        }
        var sym = new SymbolRef(symbol);
        var levelName = ParseSymbolLevel(sym);
        // find db by first level name of symbol
        var dbInfo = dbInfoList.Find(dbi => dbi.DbName == levelName);
        if (dbInfo != null)
        {
            varInfo.AccessSequence = $"{dbInfo.DbBlockRelid:X}";
            return await BrowsePlcTagBySymbolAsync(dbInfo.DbBlockTiRelid, sym, varInfo, ct).ConfigureAwait(false);
        }
        else
        {
            sym.Value = varInfo.Name;
            // Merker
            varInfo.AccessSequence = $"{Ids.NativeObjects_theMArea_Rid:X}";
            var tag = await BrowsePlcTagBySymbolAsync(0x90030000, sym, varInfo, ct).ConfigureAwait(false);
            if (tag != null)
            {
                return tag;
            }

            sym.Value = varInfo.Name;
            // Outputs
            varInfo.AccessSequence = $"{Ids.NativeObjects_theQArea_Rid:X}";
            tag = await BrowsePlcTagBySymbolAsync(0x90020000, sym, varInfo, ct).ConfigureAwait(false);
            if (tag != null)
            {
                return tag;
            }

            sym.Value = varInfo.Name;
            // Inputs
            varInfo.AccessSequence = $"{Ids.NativeObjects_theIArea_Rid:X}";
            tag = await BrowsePlcTagBySymbolAsync(0x90010000, sym, varInfo, ct).ConfigureAwait(false);
            if (tag != null)
            {
                return tag;
            }
            // TODO: implement s5timers and counters... no one uses them anymore anyway
        }
        return null;
    }

    internal sealed class BrowseEntry
    {
        public string Name { get; set; } = string.Empty;
        public uint Softdatatype { get; set; }
        public uint LID { get; set; }
        public uint SymbolCrc { get; set; }
        public string AccessSequence { get; set; } = string.Empty;
    };

    internal sealed class BrowseData
    {
        public string DbName { get; set; } = string.Empty;                                          // Name of the datablock
        public uint DbNumber { get; set; }                                        // Number of the datablock
        public uint DbBlockRelid { get; set; }                                   // RID of the datablock
        public uint DbBlockTiRelid { get; set; }                                // Type-Info RID of the datablock
        public List<BrowseEntry> Variables { get; private set; } = [];   // Variables inside the datablock
    };

    internal sealed class DatablockInfo
    {
        public string DbName { get; set; } = string.Empty;                                          // Name of the datablock
        public uint DbNumber { get; set; }                                        // Number of the datablock
        public uint DbBlockRelid { get; set; }                                   // RID of the datablock
        public uint DbBlockTiRelid { get; set; }                                // Type-Info RID of the datablock
    };

    internal async Task<(int res, List<DatablockInfo> dbInfoList)> GetListOfDatablocksAsync(CancellationToken ct = default)
    {
        int res;

        var dbInfoList = new List<DatablockInfo>();

        var exploreReq = new ExploreRequest(ProtocolVersion.V2)
        {
            ExploreId = Ids.NativeObjects_thePLCProgram_Rid,
            ExploreRequestId = Ids.None,
            ExploreChildsRecursive = 1,
            ExploreParents = 0
        };

        // Add the attributes we need in the response
        exploreReq.AddressList.Add(Ids.ObjectVariableTypeName);

        // Set filter on Id for Datablock Class RID. With this filter, we only
        // get informations from datablocks, and not other blocks we don't need here.
        var filter = new ValueStruct(Ids.Filter);
        filter.AddStructElement(Ids.FilterOperation, new ValueDInt(8)); // 8 = InstanceIOf
        filter.AddStructElement(Ids.AddressCount, new ValueUDInt(0));
        var faddress = new uint[32]; // Unknown, possible dependant on FilterOperation
        filter.AddStructElement(Ids.Address, new ValueUDIntArray(faddress));
        filter.AddStructElement(Ids.FilterValue, new ValueRID(Ids.DB_Class_Rid));

        exploreReq.FilterData = filter;

        res = SendS7plusFunctionObject(exploreReq);
        if (res != 0)
        {
            return (res, dbInfoList);
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            return (m_LastError, dbInfoList);
        }

        var exploreRes = ExploreResponse.DeserializeFromPdu(m_ReceivedPDU, true);
        res = CheckResponseWithIntegrity(exploreReq, exploreRes);
        if (res != 0)
        {
            return (res, dbInfoList);
        }

        // Get the datablock information we want further informations from.
        var objList = exploreRes!.Objects;

        foreach (var ob in objList)
        {
            // May be this check can be removed, if setting the filter to the DB_Class_Rid is working 100%.
            switch (ob.ClassId)
            {
                case Ids.DB_Class_Rid:
                    var relid = ob.RelationId;
                    var area = relid >> 16;
                    var num = relid & 0xffff;
                    if (area == 0x8a0e)
                    {
                        var name = (ValueWString)ob.GetAttribute(Ids.ObjectVariableTypeName);
                        var data = new DatablockInfo
                        {
                            DbBlockRelid = relid,
                            DbName = name.Value,
                            DbNumber = num
                        };
                        dbInfoList.Add(data);
                    }
                    break;
                default:
                    break;
            }
        }

        // Get the TypeInfo RID to RelId from the first response

        // With LID=1 we get the RID back. With this number we can explore further
        // informations of this datablock.
        // This is neccessary, because informations about instance DBs (e.g. TON) you
        // don't get by the RID of the DB, instead of exploring the TON Type RID.
        var readlist = new List<ItemAddress>();
        List<object?> values;
        List<ulong> errors;

        foreach (var data in dbInfoList)
        {
            if (data.DbNumber > 0)
            {
                // Insert the address
                var adr1 = new ItemAddress
                {
                    AccessArea = data.DbBlockRelid,
                    AccessSubArea = Ids.DB_ValueActual
                };
                adr1.LID.Add(1);
                readlist.Add(adr1);
            }
        }
        (res, values, errors) = await ReadValuesAsync(readlist, ct).ConfigureAwait(false);
        if (res != 0)
        {
            return (res, dbInfoList);
        }

        // Insert response data into the list
        for (var i = 0; i < values.Count; i++)
        {
            if (errors[i] == 0)
            {
                var rid = (ValueRID)values[i]!;
                var data = dbInfoList[i];
                data.DbBlockTiRelid = rid.Value;
                dbInfoList[i] = data;
            }
            else
            {
                // On error, set relid=0, which is then removed in the next step.
                // Should we report this for the user?
                var data = dbInfoList[i];
                data.DbBlockTiRelid = 0;
                dbInfoList[i] = data;
            }
        }

        // Remove elements with DbBlockTiRelid == 0.
        // This can occur on datablocks which are only in load memory and can't be explored.
        dbInfoList.RemoveAll(item => item.DbBlockTiRelid == 0);

        return (0, dbInfoList);
    }

    internal async Task<(int res, List<PObject> objList)> GetTypeInformationAsync(uint exploreId, CancellationToken ct = default)
    {
        int res;
        var objList = new List<PObject>();

        var exploreReq = new ExploreRequest(ProtocolVersion.V2)
        {
            ExploreId = exploreId,
            ExploreRequestId = Ids.None,
            ExploreChildsRecursive = 1,
            ExploreParents = 0
        };

        res = SendS7plusFunctionObject(exploreReq);
        if (res != 0)
        {
            return (res, objList);
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            return (m_LastError, objList);
        }

        var exploreRes = ExploreResponse.DeserializeFromPdu(m_ReceivedPDU, true);
        res = CheckResponseWithIntegrity(exploreReq, exploreRes);
        if (res != 0)
        {
            return (res, objList);
        }
        objList = exploreRes!.Objects;

        return (0, objList);
    }

    /// <summary>
    /// Requests the tag and block comments from the Plc, returned as XML strings.
    /// xml_linecomment:
    /// The returned XML format differs between between request of I/Q/M/C/T areas and datablocks:
    /// <code>
    /// I/Q/M/C/T: &lt;CommentDictionary&gt;     &lt;TagLineComments&gt;      &lt;Comment RefID="ID"&gt; &lt;DictEntry Lanuage="de-DE"&gt; ....
    /// Datablock: &lt;InterfaceLineComments&gt; &lt;Part Kind="Comments"&gt; &lt;Comment Path="ID"&gt;  &lt;DictEntry Lanuage="de-DE"&gt; ....
    /// </code>
    /// As "ID" the number for the variable identification is used.
    /// <para>
    /// xml_dbcomment:
    /// The xml-value description generated from our own value xml-serialization for WStringSparseArray. The value key is the language id.
    /// Example:
    /// <code>
    /// &lt;Value type ="WStringSparseArray"&gt;&lt;Value key="1032"&gt;DB Kommentar in german de-DE&lt;/Value&gt;&lt;Value key="1034"&gt;DB comment in english en-US&lt;/Value&gt;&lt;/Value&gt;
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="relid">The relation ID for the area you want the comments for, e.g. 0x8a0e0000+db_number, or 0x52 for M-area</param>
    /// <param name="ct">cancellation token</param>
    /// <returns>0 if no error, plus the line-comment and db-comment XML strings</returns>
    public async Task<(int res, string xml_linecomment, string xml_dbcomment)> GetCommentsXmlAsync(uint relid, CancellationToken ct = default)
    {
        int res;
        // With requesting DataInterface_InterfaceDescription, whe would be able to get all informations like the access ids and
        // datatype informations, that we get from the other browsing method. Needs to be tested which one is more efficient on network traffic or plc load.
        // If we keep use browsing for the comments, at least we would be able to read all information in one request.
        var xml_linecomment = string.Empty;
        var xml_dbcomment = string.Empty;

        var exploreReq = new ExploreRequest(ProtocolVersion.V2)
        {
            ExploreId = relid,
            ExploreRequestId = Ids.None,
            ExploreChildsRecursive = 1,
            ExploreParents = 0
        };

        // We want to know the following attributes
        exploreReq.AddressList.Add(Ids.ASObjectES_Comment);
        exploreReq.AddressList.Add(Ids.DataInterface_LineComments);

        res = SendS7plusFunctionObject(exploreReq);
        if (res != 0)
        {
            return (res, xml_linecomment, xml_dbcomment);
        }
        m_LastError = 0;
        await WaitForNewS7plusReceivedAsync(m_ReadTimeout, ct).ConfigureAwait(false);
        if (m_LastError != 0)
        {
            return (m_LastError, xml_linecomment, xml_dbcomment);
        }

        var exploreRes = ExploreResponse.DeserializeFromPdu(m_ReceivedPDU, true);
        res = CheckResponseWithIntegrity(exploreReq, exploreRes);
        if (res != 0)
        {
            return (res, xml_linecomment, xml_dbcomment);
        }

        foreach (var obj in exploreRes!.Objects)
        {
            foreach (var att in obj.Attributes)
            {
                switch (att.Key)
                {
                    case Ids.ASObjectES_Comment:
                        var att_comment = (ValueWStringSparseArray)att.Value;
                        xml_dbcomment = att_comment.ToString();
                        break;
                    case Ids.DataInterface_LineComments:
                        var att_linecomment = (ValueBlobSparseArray)att.Value;
                        var blob_sp = att_linecomment.Value;
                        // In DBs we get the data with Sparsearray key = 1, in M-Area with key = 2.
                        // For now, just take the first, don't know where the key ids are for.
                        foreach (var key in blob_sp.Keys)
                        {
                            xml_linecomment = BlobDecompressor.Decompress(blob_sp[key].Value, 4); // Offset of 4, as we have a header for the zlib dictionary version
                            break;
                        }
                        break;
                    default:
                        break;
                }
            }
        }
        return (0, xml_linecomment, xml_dbcomment);
    }

    [GeneratedRegex(@"^\[(-?\d+)\]")]
    private static partial Regex SingleDimensionIndexRegex { get; }

    [GeneratedRegex(@"^\[( ?-?\d+ ?(, ?-?\d+ ?)+)\]")]
    private static partial Regex MultiDimensionIndexRegex { get; }
}
#endregion
