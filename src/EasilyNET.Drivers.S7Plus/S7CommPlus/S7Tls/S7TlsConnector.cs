// SPDX-License-Identifier: LGPL-3.0-or-later
// Derived from thomas-v2/S7CommPlusDriver, Copyright (C) 2023 Thomas Wiens. See LICENSE-LGPL-3.0.txt.
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
// S7CommPlus 自身也定义了 ProtocolVersion（V1/V2/V3），与 BouncyCastle 的同名类型冲突，故起别名。
using BcProtocolVersion = Org.BouncyCastle.Tls.ProtocolVersion;

namespace EasilyNET.Drivers.S7Plus.S7CommPlus.S7Tls;

internal interface IConnectorCallback
{
    void WriteData(byte[] pData, int dataLength);
    void OnDataAvailable();
}

/// <summary>
/// 基于 BouncyCastle 的纯托管 TLS 1.3 连接器，替代原先依赖原生 libssl/libcrypto 的实现。
/// 以非阻塞模式运行：密文由上层 ISO-on-TCP 传输层喂入 / 取出，明文经回调交付。
/// 提供 S7CommPlus 合法化所需的 RFC 5705 "EXPERIMENTAL_OMS" 密钥材料导出。
/// 对协议状态的访问以 <see cref="m_sync"/> 串行化，使发送线程与接收线程可安全并发（参见 #70/#81）。
/// </summary>
internal sealed class S7TlsConnector
{
    // 单个 ISO PDU 内最多塞入的密文字节数。TLS 是字节流，跨 ISO 包切分对两端透明，取保守值即可。
    private const int OutputChunkSize = 1024;
    private readonly IConnectorCallback m_DataSink;
    private readonly TlsClientProtocol m_protocol;
    private readonly S7TlsClient m_client;
    private readonly Lock m_sync = new();

    private readonly byte[] m_buffer = new byte[8192];
    private int m_bytesAvailable;

    // 握手完成前先缓存待发送的应用层数据，握手结束后统一冲刷
    private readonly List<byte[]> m_pendingAppWrites = [];
    private bool m_handshakeFlushed;

    public S7TlsConnector(IConnectorCallback dataSink)
    {
        m_DataSink = dataSink;
        var crypto = new BcTlsCrypto(new SecureRandom());
        m_client = new S7TlsClient(crypto);
        m_protocol = new TlsClientProtocol();
    }

    /// <summary>开始 TLS 握手，立即把 ClientHello 推送到传输层。</summary>
    public void ExpectConnect()
    {
        lock (m_sync)
        {
            m_protocol.Connect(m_client);
            DrainOutput();
        }
    }

    /// <summary>提交要加密发送的明文（应用层数据）。</summary>
    public void Write(byte[] pData, int dataLen)
    {
        lock (m_sync)
        {
            if (m_protocol.IsHandshaking)
            {
                // 握手尚未完成，BouncyCastle 不允许此时写应用数据，先缓存
                var copy = new byte[dataLen];
                Buffer.BlockCopy(pData, 0, copy, 0, dataLen);
                m_pendingAppWrites.Add(copy);
                return;
            }
            m_protocol.WriteApplicationData(pData, 0, dataLen);
            DrainOutput();
        }
    }

    /// <summary>喂入从传输层收到的密文，驱动握手并解出明文。</summary>
    public void ReadCompleted(byte[] pData, int dataLen)
    {
        lock (m_sync)
        {
            m_protocol.OfferInput(pData, 0, dataLen);

            // 握手过程中产生的应答（如 ClientHello 之后的 client Finished）需要回发
            DrainOutput();

            // 握手刚完成：冲刷此前缓存的应用层数据
            if (!m_protocol.IsHandshaking && !m_handshakeFlushed)
            {
                m_handshakeFlushed = true;
                foreach (var data in m_pendingAppWrites)
                {
                    m_protocol.WriteApplicationData(data, 0, data.Length);
                }
                m_pendingAppWrites.Clear();
                DrainOutput();
            }

            // 解出的明文交付给上层。交付（OnDataReceived）只做解析入队，不会重入本连接器，
            // 故可在锁内安全调用。
            int avail;
            while ((avail = m_protocol.GetAvailableInputBytes()) > 0)
            {
                var n = Math.Min(avail, m_buffer.Length);
                m_protocol.ReadInput(m_buffer, 0, n);
                m_bytesAvailable = n;
                while (m_bytesAvailable > 0)
                {
                    m_DataSink.OnDataAvailable();
                }
            }
        }
    }

    /// <summary>上层取走已解密的明文。</summary>
    public int Receive(ref byte[] pData, int dataLength)
    {
        var bytesRead = Math.Min(m_bytesAvailable, dataLength);
        Buffer.BlockCopy(m_buffer, 0, pData, 0, bytesRead);
        if (bytesRead != m_bytesAvailable)
        {
            Buffer.BlockCopy(m_buffer, bytesRead, m_buffer, 0, m_bytesAvailable - bytesRead);
        }
        m_bytesAvailable -= bytesRead;
        return bytesRead;
    }

    /// <summary>
    /// 导出 OMS 合法化所需的 32 字节密钥材料（RFC 5705，标签 EXPERIMENTAL_OMS）。
    /// 等价于原 OpenSSL 调用 SSL_export_keying_material(..., use_context=0)，故 context 传 null。
    /// </summary>
    public byte[]? GetOMSExporterSecret()
    {
        lock (m_sync)
        {
            var ctx = m_client.TlsContext;
            return ctx?.ExportKeyingMaterial("EXPERIMENTAL_OMS", null, 32);
        }
    }

    // 把待发送的密文按 ISO PDU 友好的块大小排空到传输层。调用方必须持有 m_sync。
    private void DrainOutput()
    {
        int avail;
        while ((avail = m_protocol.GetAvailableOutputBytes()) > 0)
        {
            var n = Math.Min(avail, OutputChunkSize);
            var buf = new byte[n];
            var read = m_protocol.ReadOutput(buf, 0, n);
            if (read <= 0)
            {
                break;
            }
            if (read == n)
            {
                m_DataSink.WriteData(buf, read);
            }
            else
            {
                var trimmed = new byte[read];
                Buffer.BlockCopy(buf, 0, trimmed, 0, read);
                m_DataSink.WriteData(trimmed, read);
            }
        }
    }
}

/// <summary>
/// S7-1200/1500 专用 TLS 客户端：强制 TLS 1.3 + AES-GCM，接受任意服务器证书（PLC 用自签名证书），
/// 不提供客户端证书（认证由上层 OMS 合法化完成）。
/// </summary>
internal sealed class S7TlsClient(TlsCrypto crypto) : DefaultTlsClient(crypto)
{

    // 暴露握手上下文，用于导出 OMS 密钥材料
    public TlsContext TlsContext => m_context;

    // 强制 TLS 1.3：S7CommPlus on IsoOnTCP 依赖 TLS1.3 GCM 固定（+17 字节）的报文长度增量来做分片
    protected override BcProtocolVersion[] GetSupportedVersions() => BcProtocolVersion.TLSv13.Only();

    // 仅启用 TLS1.3 GCM 套件，排除 ChaCha20-Poly1305（长度增量须可预测）
    protected override int[] GetSupportedCipherSuites() =>
    [
        CipherSuite.TLS_AES_256_GCM_SHA384,
        CipherSuite.TLS_AES_128_GCM_SHA256,
    ];

    public override TlsAuthentication GetAuthentication() => new S7TlsAuthentication();
}

// PLC 使用自签名证书，原实现不做校验；此处同样接受任意服务器证书，且不提供客户端证书。
internal sealed class S7TlsAuthentication : TlsAuthentication
{
    public void NotifyServerCertificate(TlsServerCertificate serverCertificate) { }

    public TlsCredentials? GetClientCredentials(CertificateRequest certificateRequest) => null;
}
