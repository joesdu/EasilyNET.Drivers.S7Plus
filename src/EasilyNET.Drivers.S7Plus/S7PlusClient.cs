// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Joe Du. See LICENSE.
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using System.Text.RegularExpressions;

namespace EasilyNET.Drivers.S7Plus;

/// <summary>
///     西门子 S7-1200/S7-1500 符号寻址客户端（S7CommPlus 协议，TLS 加密），<b>异步优先</b>。
///     地址直接填 PLC 中的符号变量名，如 <c>"DataBlock_1.Temperature"</c>；
///     需在 TIA Portal 中开启“安全的 PG/PC 及 HMI 通信”，且数据块为优化访问。
///     <para xml:lang="en">
///         Async-first symbolic-addressing client for Siemens S7-1200/S7-1500 over the S7CommPlus protocol (TLS encrypted).
///         Addresses are PLC symbol names such as <c>"DataBlock_1.Temperature"</c>; the PLC must have
///         "secure PG/PC and HMI communication" enabled and the data blocks must use optimized access.
///     </para>
/// </summary>
/// <remarks>
///     <b>自上而下的真异步实现</b>：从底层 socket 收发（<c>Socket.*Async</c>）、TLS 密文泵、ISO 帧组装，到
///     S7CommPlus 请求-响应等待，全链路均为真正的 <c>await</c>——无后台线程、无忙等待轮询、无 <c>Task.Run</c> 包装。
///     接收由单个异步“接收泵”驱动，响应通过异步信号量交付，<see cref="CancellationToken" /> 贯穿全程可随时取消。
///     所有 I/O（连接/读/写/断开）通过同一异步信号量串行化，可安全地从多个调用方并发 <c>await</c>（自动排队），
///     避免同一连接上的并发请求导致协议序列号错乱。
///     <para xml:lang="en">
///         Genuine end-to-end async: from socket I/O (<c>Socket.*Async</c>), the TLS ciphertext pump and ISO framing,
///         up to S7CommPlus request/response waiting — everything is real <c>await</c>, with no background thread,
///         no busy-wait polling and no <c>Task.Run</c> wrapping. A single async receive pump drives reception,
///         responses are delivered via an async semaphore, and the <see cref="CancellationToken" /> flows throughout.
///         All I/O is serialized through one async semaphore, so the client is safe to <c>await</c> from multiple
///         callers concurrently (they queue), preventing protocol sequence-number corruption on one connection.
///     </para>
/// </remarks>
public sealed partial class S7PlusClient : IAsyncDisposable
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private readonly string host;
    private readonly string username;
    private readonly string password;
    private readonly int timeoutMs;
    private readonly ILogger logger;

    // 异步信号量：串行化全部 I/O（连接/读/写/断开）。协议要求同一连接的请求串行，
    // 因此用它替代同步 lock，并允许在持锁期间 await 线程池上的阻塞调用。
    private readonly SemaphoreSlim gate = new(1, 1);

    // 点表地址(符号) → 已解析的 PlcTag（含 ItemAddress 与 Softdatatype）。
    // 按需懒解析，断开时清空；避免连接后对整个 PLC 做全量 Browse。
    // 值为 null 表示该符号在当前连接中解析失败（未找到），本连接周期内不再重复解析。
    private readonly ConcurrentDictionary<string, PlcTag?> resolvedCache = new(StringComparer.OrdinalIgnoreCase);
#pragma warning disable IDE0079 // 请删除不必要的忽略
    // CA2213 误报：该字段在 DisposeAsync 中通过 Interlocked.Exchange(ref connection, null) 搬到局部变量后 DisposeAsync 释放，
    // 分析器无法追踪这层原子交换的间接释放。此模式是为防止多路径并发下的重复释放/竞态而刻意保留，不能改为直接对字段调用。
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Disposed in DisposeAsync via Interlocked.Exchange to a local; thread-safe by design.")]
    private S7CommPlusConnection? connection;
#pragma warning restore IDE0079 // 请删除不必要的忽略
    private volatile bool isConnected;
    private bool disposed;

    /// <summary>
    ///     创建客户端。
    ///     <para xml:lang="en">Creates the client.</para>
    /// </summary>
    /// <param name="host">PLC 的 IP 地址，例如 <c>"192.168.0.1"</c>。<para xml:lang="en">PLC IP address, e.g. <c>"192.168.0.1"</c>.</para></param>
    /// <param name="options">连接选项（用户名/密码/超时）。<para xml:lang="en">Connection options (user name / password / timeout).</para></param>
    /// <param name="logger">可选日志记录器；为 <see langword="null" /> 时不输出日志。<para xml:lang="en">Optional logger; no logging when <see langword="null" />.</para></param>
    public S7PlusClient(string host, S7PlusClientOptions? options = null, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        this.host = host;
        username = options?.Username ?? string.Empty;
        password = options?.Password ?? string.Empty;
        timeoutMs = options is { TimeoutMs: > 0 } ? options.TimeoutMs : 5000;
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    ///     当前是否处于已连接状态。
    ///     <para xml:lang="en">Whether the client is currently connected.</para>
    /// </summary>
    public bool Connected => connection is not null && isConnected;

    #region connect

    /// <summary>
    ///     异步连接到 PLC。已连接时直接返回 <see langword="true" />。
    ///     <para xml:lang="en">Connects to the PLC asynchronously. Returns <see langword="true" /> immediately if already connected.</para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。<para xml:lang="en">Cancellation token.</para></param>
    /// <returns>连接成功返回 <see langword="true" />。<para xml:lang="en"><see langword="true" /> on success.</para></returns>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ConnectLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // 调用方须已持有 gate。
    private async Task<bool> ConnectLockedAsync(CancellationToken cancellationToken)
    {
        if (isConnected)
        {
            return true;
        }
        S7CommPlusConnection? conn = null;
        try
        {
            conn = new S7CommPlusConnection(logger);
            var res = await conn.ConnectAsync(host, password, username, timeoutMs, cancellationToken).ConfigureAwait(false);
            if (res == 0)
            {
                connection = conn;
                conn = null; // 所有权已转给字段，防止 finally 二次释放
                isConnected = true;
                resolvedCache.Clear();
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("S7PlusClient: connected to {Host}", host);
                }
                return true;
            }
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("S7PlusClient: connect to {Host} failed, res=0x{Res:X}", host, res);
            }
            return false;
        }
        catch (OperationCanceledException)
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "S7PlusClient: connect to {Host} error", host);
            }
            await DisconnectCoreAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            // 未转移所有权（连接失败或抛异常）时统一释放，堵住异常路径下 conn 泄漏
            if (conn is not null)
            {
                try { await conn.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    ///     异步断开与 PLC 的连接（协议层优雅关闭并释放底层资源）。
    ///     <para xml:lang="en">Disconnects from the PLC asynchronously (graceful protocol shutdown and resource release).</para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。<para xml:lang="en">Cancellation token.</para></param>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            isConnected = false;
            var conn = Interlocked.Exchange(ref connection, null);
            if (conn is not null)
            {
                // 优雅断开：删除会话后释放底层连接（全异步）
                try { await conn.DisconnectAsync(cancellationToken).ConfigureAwait(false); } catch { /* 优雅关闭尽力而为 */ }
                try { await conn.DisposeAsync().ConfigureAwait(false); } catch { /* 释放 S7Client/Socket */ }
            }
            resolvedCache.Clear();
        }
        finally
        {
            gate.Release();
        }
    }

    // best-effort 断开（用于 I/O 错误清理）：不做会话删除的网络往返，直接异步释放底层连接。
    private async Task DisconnectCoreAsync()
    {
        isConnected = false;
        // 先把字段置空，避免其它线程在释放过程中再拿到正在销毁的连接
        var conn = Interlocked.Exchange(ref connection, null);
        if (conn is not null)
        {
            try { await conn.DisposeAsync().ConfigureAwait(false); } catch { /* 释放 S7Client/Socket，避免重连泄漏句柄 */ }
        }
        // 清空解析缓存：重连后将针对新的连接对象重新解析符号
        resolvedCache.Clear();
    }

    #endregion

    #region resolve

    /// <summary>
    ///     按需把点表地址(PLC 符号)解析为 <see cref="PlcTag" /> 并缓存。
    ///     兼容上位机/组态软件导出的带前缀完整地址（如 <c>PLC_1.Blocks.DB1.test1</c>）：
    ///     先按完整地址解析，失败则逐级剥离最前面一段标识符前缀后重试，直到 PLC 识别或无前缀可剥离。
    /// </summary>
    private async Task<PlcTag?> ResolveAsync(string address, CancellationToken ct)
    {
        if (resolvedCache.TryGetValue(address, out var cached))
        {
            return cached;
        }
        var conn = connection;
        if (conn is null)
        {
            return null;
        }
        PlcTag? tag = null;
        try
        {
            tag = await ResolvePlcTagAsync(conn, address, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "S7PlusClient: resolve symbol ({Address}) error", address);
            }
        }
        if (tag is null)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("S7PlusClient: symbol ({Address}) not found in PLC", address);
            }
        }
        // 缓存成功或失败结果（断开时清空），避免每个读取周期重复解析同一符号
        resolvedCache[address] = tag;
        return tag;
    }

    private static async Task<PlcTag?> ResolvePlcTagAsync(S7CommPlusConnection conn, string address, CancellationToken ct)
    {
        var symbol = address;
        while (true)
        {
            var tag = await conn.GetPlcTagBySymbolAsync(symbol, ct).ConfigureAwait(false);
            if (tag is not null)
            {
                return tag;
            }
            // 引号开头视为已是块名（可能含特殊字符），不再继续剥离前缀
            if (symbol.Length == 0 || symbol[0] == '"')
            {
                return null;
            }
            var dot = symbol.IndexOf('.');
            if (dot < 0)
            {
                return null; // 没有更多前缀可剥离
            }
            symbol = symbol[(dot + 1)..];
        }
    }

    #endregion

    #region read

    /// <summary>
    ///     异步批量读取多个符号；未连接时会自动尝试连接。
    ///     <para xml:lang="en">Asynchronously reads multiple symbols in one batch; automatically connects if not yet connected.</para>
    /// </summary>
    /// <param name="symbols">PLC 符号列表。<para xml:lang="en">The PLC symbols to read.</para></param>
    /// <returns>与输入顺序一致的读取结果。<para xml:lang="en">Read results in the same order as the input.</para></returns>
    public Task<IReadOnlyList<S7TagValue>> ReadAsync(params string[] symbols) => ReadAsync(symbols, CancellationToken.None);

    /// <summary>
    ///     异步批量读取多个符号；未连接时会自动尝试连接。
    ///     <para xml:lang="en">Asynchronously reads multiple symbols in one batch; automatically connects if not yet connected.</para>
    /// </summary>
    /// <param name="symbols">PLC 符号列表。<para xml:lang="en">The PLC symbols to read.</para></param>
    /// <param name="cancellationToken">取消令牌。<para xml:lang="en">Cancellation token.</para></param>
    public async Task<IReadOnlyList<S7TagValue>> ReadAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(symbols);
        var list = symbols as IList<string> ?? [.. symbols];
        if (list.Count == 0)
        {
            return [];
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return !Connected && !await ConnectLockedAsync(cancellationToken).ConfigureAwait(false)
                ? []
                : await ReadCoreAsync(list, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消仅中断了等待响应，请求可能已发往 PLC；其回包会遗留在接收队列，
            // 使后续请求的响应错位（序列号/完整性校验随后会强制断连）。这里主动丢弃连接，下次自动重连。
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "S7PlusClient: read error");
            }
            return [];
        }
        finally
        {
            gate.Release();
        }
    }

    // 调用方须已持有 gate。
    private async Task<IReadOnlyList<S7TagValue>> ReadCoreAsync(IList<string> symbols, CancellationToken ct)
    {
        var conn = connection;
        if (conn is null)
        {
            return [];
        }
        var addressList = new List<ItemAddress>(symbols.Count);
        var requested = new List<string>(symbols.Count);
        var tagList = new List<PlcTag>(symbols.Count);
        foreach (var symbol in symbols)
        {
            var resolved = await ResolveAsync(symbol, ct).ConfigureAwait(false);
            if (resolved is not null)
            {
                addressList.Add(resolved.Address);
                requested.Add(symbol);
                tagList.Add(resolved);
            }
        }
        if (addressList.Count == 0)
        {
            return [];
        }
        var (res, values, errors) = await conn.ReadValuesAsync(addressList, ct).ConfigureAwait(false);

        // 任意非零返回都视为通信/会话异常（如 PLC 主动终止会话、读超时）：
        // 断开以触发下次重连，而不是返回一堆 null 并永久卡死。
        if (res != 0)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("S7PlusClient: ReadValues res=0x{Res:X}", res);
            }
            await DisconnectCoreAsync().ConfigureAwait(false);
            return [];
        }
        var now = DateTime.Now;
        var result = new List<S7TagValue>(requested.Count);
        for (var i = 0; i < requested.Count; i++)
        {
            string? strValue = null;
            if (errors is not null && i < errors.Count && errors[i] == 0 &&
                values is not null && i < values.Count && values[i] is not null)
            {
                strValue = FormatValue(tagList[i], values[i]!);
            }
            result.Add(new(requested[i], strValue, strValue is not null, now));
        }
        return result;
    }

    // ReadValues 返回的是协议层 PValue。同一个 PValue 类型可能对应不同 PLC 数据类型
    // （如 Date 与 UInt 都是 ValueUInt、String 与字节数组都是 ValueUSIntArray），
    // 因此字符串/日期/时间等必须借助已解析的 PlcTag（含 Softdatatype）来正确解码。
    private static string? FormatValue(PlcTag tag, object value)
    {
        var ic = CultureInfo.InvariantCulture;

        // ① 字符串/字符/日期时间/DTL/指针等：协议层与数值同型或为数组，必须按 PlcTag 类型解码
        switch (tag)
        {
            case PlcTagString or PlcTagWString or PlcTagChar or PlcTagWChar
                or PlcTagDate or PlcTagTimeOfDay or PlcTagTime or PlcTagDateAndTime
                or PlcTagS5Time or PlcTagLTime or PlcTagLTOD or PlcTagLDT or PlcTagDTL
                or PlcTagPointer or PlcTagAny
                // 字符串/日期时间数组：协议层为 ValueUSIntArray，必须按 PlcTag 语义解码，
                // 不能走 ② 的 FormatArray（那会把底层字节当数值逗号拼接）
                or PlcTagStringArray or PlcTagDateAndTimeArray:
                tag.ProcessReadResult(value, 0);
                if (tag.Quality != PlcTagQC.TAG_QUALITY_GOOD)
                {
                    return null;
                }
                return tag switch
                {
                    PlcTagString s => s.Value,
                    PlcTagWString s => s.Value,
                    PlcTagChar c => c.Value.ToString(),
                    PlcTagWChar c => c.Value.ToString(),
                    // 日期/日期时间统一为 yyyy-MM-dd HH:mm:ss
                    PlcTagDate d => d.Value.ToString(DateTimeFormat, ic),
                    PlcTagDateAndTime d => d.Value.ToString(DateTimeFormat, ic),
                    PlcTagDTL d => d.Value.ToString(DateTimeFormat, ic),
                    PlcTagLDT d => DateTime.UnixEpoch.AddTicks((long)(d.Value / 100)).ToString(DateTimeFormat, ic),
                    // 字符串/日期时间数组：逐元素语义格式化后逗号拼接
                    PlcTagStringArray sa => string.Join(",", sa.Value),
                    PlcTagDateAndTimeArray da => string.Join(",", da.Value.Select(d => d.ToString(DateTimeFormat, ic))),
                    // 时长/时刻类(Time/LTime/S5Time/TimeOfDay/LTOD)与指针等：保留可读格式，去质量前缀
                    _ => StripQuality(tag.ToString())
                };
            default:
                break;
        }

        // ② 标量/数组单次分发：标量分支在前（高频数值采集最常见），命中即返回，
        // 避免原先“先扫一遍数组分支再扫一遍标量分支”的双重 isinst 扫描（每个 tag 约省一半类型测试）。
        return FormatPValue(value, ic);
    }

    private static string? FormatPValue(object value, CultureInfo ic) =>
        value switch
        {
            // 标量（最常见）
            ValueBool v => v.Value ? "1" : "0",
            ValueByte v => v.Value.ToString(ic),
            ValueUSInt v => v.Value.ToString(ic),
            ValueSInt v => v.Value.ToString(ic),
            ValueUInt v => v.Value.ToString(ic),
            ValueInt v => v.Value.ToString(ic),
            ValueWord v => v.Value.ToString(ic),
            ValueUDInt v => v.Value.ToString(ic),
            ValueDInt v => v.Value.ToString(ic),
            ValueDWord v => v.Value.ToString(ic),
            ValueULInt v => v.Value.ToString(ic),
            ValueLInt v => v.Value.ToString(ic),
            ValueLWord v => v.Value.ToString(ic),
            ValueReal v => v.Value.ToString(ic),
            ValueLReal v => v.Value.ToString(ic),
            ValueTimestamp v => v.Value.ToString(ic),
            ValueTimespan v => v.Value.ToString(ic),
            ValueRID v => v.Value.ToString(ic),
            // 数组
            ValueBoolArray a => string.Join(",", a.Value.Select(b => b ? "1" : "0")),
            ValueByteArray a => string.Join(",", a.Value),
            ValueUSIntArray a => string.Join(",", a.Value),
            ValueSIntArray a => string.Join(",", a.Value),
            ValueWordArray a => string.Join(",", a.Value),
            ValueUIntArray a => string.Join(",", a.Value),
            ValueIntArray a => string.Join(",", a.Value),
            ValueDWordArray a => string.Join(",", a.Value),
            ValueUDIntArray a => string.Join(",", a.Value),
            ValueDIntArray a => string.Join(",", a.Value),
            ValueLWordArray a => string.Join(",", a.Value),
            ValueULIntArray a => string.Join(",", a.Value),
            ValueLIntArray a => string.Join(",", a.Value),
            ValueRealArray a => string.Join(",", a.Value.Select(x => x.ToString(ic))),
            ValueLRealArray a => string.Join(",", a.Value.Select(x => x.ToString(ic))),
            ValueTimestampArray a => string.Join(",", a.Value),
            ValueTimespanArray a => string.Join(",", a.Value),
            _ => value.ToString()
        };

    // PlcTag.ToString() 形如 "QC: value"（两位十六进制质量码 + ": "），去掉前缀只留值
    private static string StripQuality(string? s)
    {
        if (s is null)
        {
            return string.Empty;
        }
        var idx = s.IndexOf(": ", StringComparison.Ordinal);
        return idx >= 0 ? s[(idx + 2)..] : s;
    }

    #endregion

    #region write

    /// <summary>
    ///     异步写入单个符号的值（按 PLC 中该符号的真实数据类型编码）。
    ///     <para xml:lang="en">Asynchronously writes a single symbol (encoded by the symbol's real data type in the PLC).</para>
    /// </summary>
    /// <param name="symbol">PLC 符号。<para xml:lang="en">The PLC symbol.</para></param>
    /// <param name="value">字符串形式的写入值。<para xml:lang="en">The value to write, as a string.</para></param>
    /// <param name="cancellationToken">取消令牌。<para xml:lang="en">Cancellation token.</para></param>
    /// <returns>写入是否成功提交。<para xml:lang="en">Whether the write was committed successfully.</para></returns>
    public Task<bool> WriteAsync(string symbol, string value, CancellationToken cancellationToken = default) =>
        WriteAsync([new(symbol, value)], cancellationToken);

    /// <summary>
    ///     异步批量写入多个符号。
    ///     <para xml:lang="en">Asynchronously writes multiple symbols in one batch.</para>
    /// </summary>
    /// <param name="writes">符号到写入值的映射。<para xml:lang="en">Mapping from symbol to value.</para></param>
    /// <param name="cancellationToken">取消令牌。<para xml:lang="en">Cancellation token.</para></param>
    /// <returns>写入是否成功提交。<para xml:lang="en">Whether the write was committed successfully.</para></returns>
    public async Task<bool> WriteAsync(IEnumerable<KeyValuePair<string, string>> writes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(writes);
        var items = writes as IList<KeyValuePair<string, string>> ?? [.. writes];
        if (items.Count == 0)
        {
            return false;
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (Connected || await ConnectLockedAsync(cancellationToken).ConfigureAwait(false)) && await WriteCoreAsync(items, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 同 ReadAsync：取消后连接的协议状态不可知，丢弃以触发下次重连
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "S7PlusClient: write error");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    // 调用方须已持有 gate。
    private async Task<bool> WriteCoreAsync(IList<KeyValuePair<string, string>> items, CancellationToken ct)
    {
        var conn = connection;
        if (conn is null)
        {
            return false;
        }
        // 先解析全部符号，保留 (tag, 用户符号, 值)；解析失败的项跳过
        var resolvedItems = new List<(PlcTag Tag, string Symbol, string Value)>(items.Count);
        foreach (var item in items)
        {
            var resolved = await ResolveAsync(item.Key, ct).ConfigureAwait(false);
            if (resolved is null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("S7PlusClient: cannot write {Symbol}={Value}, address not resolved", item.Key, item.Value);
                }
                continue;
            }
            resolvedItems.Add((resolved, item.Key, item.Value));
        }
        if (resolvedItems.Count == 0)
        {
            return false;
        }

        // DTL 写入依赖正确的接口时间戳，而它只能来自一次实际读取（符号解析不会填充）。
        // 对尚未获得时间戳的 DTL tag 先预读一次以填充，之后同一连接内复用（tag 实例已缓存）。
        var dtlToPrime = resolvedItems.Select(static x => x.Tag).OfType<PlcTagDTL>().Where(static d => !d.InterfaceTimestampKnown).Distinct().ToList();
        if (dtlToPrime.Count > 0)
        {
            var primeAddrs = dtlToPrime.ConvertAll(static d => d.Address);
            var (pres, pvalues, perrors) = await conn.ReadValuesAsync(primeAddrs, ct).ConfigureAwait(false);
            if (pres == 0 && pvalues is not null)
            {
                for (var i = 0; i < dtlToPrime.Count && i < pvalues.Count; i++)
                {
                    if (pvalues[i] is not null)
                    {
                        var err = perrors is not null && i < perrors.Count ? perrors[i] : 0;
                        dtlToPrime[i].ProcessReadResult(pvalues[i]!, err);
                    }
                }
            }
        }

        var addressList = new List<ItemAddress>(resolvedItems.Count);
        var valueList = new List<PValue>(resolvedItems.Count);
        foreach (var (tag, symbol, value) in resolvedItems)
        {
            // 使用解析得到的实际数据类型编码，避免误当作 REAL 处理
            var pval = CreateWriteValue(tag, symbol, value);
            if (pval is not null)
            {
                addressList.Add(tag.Address);
                valueList.Add(pval);
            }
        }
        if (addressList.Count == 0)
        {
            return false;
        }
        try
        {
            var (res, _) = await conn.WriteValuesAsync(addressList, valueList, ct).ConfigureAwait(false);
            if (res != 0)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("S7PlusClient: WriteValues res=0x{Res:X}", res);
                }
                await DisconnectCoreAsync().ConfigureAwait(false);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "S7PlusClient: write error");
            }
            await DisconnectCoreAsync().ConfigureAwait(false);
            return false;
        }
    }

    private PValue? CreateWriteValue(PlcTag tag, string symbol, string value)
    {
        var softDt = tag.Datatype;
        try
        {
            // 字符串类型必须交给 PlcTag 生成带 [maxLen][actLen] 头部的正确线上编码：
            // S7 String 是单字节数组(ValueUSIntArray)、WString 是 16 位字数组(ValueUIntArray)，
            // 直接发 ValueWString 会因类型不符被 PLC 静默拒收（这正是读得出却写不进的根因）。
            // 字符串与日期/时间类型统一路由到 PlcTag.GetWriteValue()：其编码是读路径解码的逆运算，
            // 由 PlcTag 保证与 PLC 变量真实结构一致（标量类型仍走下方 softDt 分支，编码等价且更直接）。
            switch (tag)
            {
                case PlcTagString s:
                    s.Value = value;
                    return s.GetWriteValue();
                case PlcTagWString ws:
                    ws.Value = value;
                    return ws.GetWriteValue();
                // DATE：日期（自 1990-01-01 起的天数），接受 "yyyy-MM-dd[ HH:mm:ss]"
                case PlcTagDate d:
                    d.Value = ParseDateTimeInvariant(value);
                    return d.GetWriteValue();
                // DATE_AND_TIME：BCD 编码，接受 "yyyy-MM-dd HH:mm:ss[.fff]"
                case PlcTagDateAndTime dt:
                    dt.Value = ParseDateTimeInvariant(value);
                    return dt.GetWriteValue();
                // LDT：自 1970 epoch 起的纳秒数，写入为读路径 (Value/100→ticks) 的逆运算
                case PlcTagLDT ldt:
                    ldt.Value = (ulong)((ParseDateTimeInvariant(value) - DateTime.UnixEpoch).Ticks * 100);
                    return ldt.GetWriteValue();
                // DTL：接口时间戳必须先由一次读取填充（见 WriteCoreAsync 的预读），否则包可能被 PLC 拒收
                case PlcTagDTL dtl:
                    if (!dtl.InterfaceTimestampKnown)
                    {
                        throw new InvalidOperationException("DTL write requires a prior read to obtain the interface timestamp.");
                    }
                    var dtlDt = ParseDateTimeInvariant(value);
                    dtl.ValueNanosecond = (uint)(dtlDt.Ticks % TimeSpan.TicksPerSecond * 100);
                    dtl.Value = dtlDt;
                    return dtl.GetWriteValue();
                // TIME_OF_DAY：自 00:00:00 起的毫秒数，接受 "HH:mm:ss[.fff]"
                case PlcTagTimeOfDay tod:
                    tod.Value = (uint)(ParseTimeOfDayNs(value) / 1_000_000L);
                    return tod.GetWriteValue();
                // LTOD：自 00:00:00 起的纳秒数，接受 "HH:mm:ss[.fffffffff]"
                case PlcTagLTOD ltod:
                    ltod.Value = (ulong)ParseTimeOfDayNs(value);
                    return ltod.GetWriteValue();
                // TIME：带符号毫秒。接受纯整数(毫秒)或西门子时长字面量，如 "1s500ms"、"-2h"
                case PlcTagTime t:
                    t.Value = (int)(ParseDurationNs(value, 1_000_000L) / 1_000_000L);
                    return t.GetWriteValue();
                // LTIME：带符号纳秒。接受纯整数(纳秒)或时长字面量，如 "1s500ms"、"1us"
                case PlcTagLTime lt:
                    lt.Value = ParseDurationNs(value, 1L);
                    return lt.GetWriteValue();
                // S5TIME：毫秒（自动选择时基），接受纯整数(毫秒)或时长字面量，如 "9990ms"、"2s"
                case PlcTagS5Time s5:
                    SetS5Time(s5, ParseDurationNs(value, 1_000_000L) / 1_000_000L);
                    return s5.GetWriteValue();
            }
            return softDt switch
            {
                Softdatatype.S7COMMP_SOFTDATATYPE_BOOL or Softdatatype.S7COMMP_SOFTDATATYPE_BBOOL =>
                    new ValueBool(value is "1" or "true" or "True"),
                // BYTE 与 USINT 在 S7CommPlus 中是不同的数据类型 ID（Byte=0x0a, USInt=0x02），不能混用，
                // 否则 PLC 因类型不符静默丢弃写入（与读路径 PlcTagByte→ValueByte 保持一致）。
                Softdatatype.S7COMMP_SOFTDATATYPE_BYTE =>
                    new ValueByte(byte.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_USINT =>
                    new ValueUSInt(byte.Parse(value, CultureInfo.InvariantCulture)),
                // CHAR/WCHAR 取首字符，与读路径 PlcTagChar→ValueUSInt、PlcTagWChar→ValueUInt 对应。
                Softdatatype.S7COMMP_SOFTDATATYPE_CHAR =>
                    new ValueUSInt((byte)value[0]),
                Softdatatype.S7COMMP_SOFTDATATYPE_WCHAR =>
                    new ValueUInt(Convert.ToUInt16(value[0])),
                Softdatatype.S7COMMP_SOFTDATATYPE_WORD =>
                    new ValueWord(ushort.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_DWORD =>
                    new ValueDWord(uint.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_LWORD =>
                    new ValueLWord(ulong.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_INT =>
                    new ValueInt(short.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_DINT =>
                    new ValueDInt(int.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_REAL =>
                    new ValueReal(float.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_LREAL =>
                    new ValueLReal(double.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_SINT =>
                    new ValueSInt(sbyte.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_UINT =>
                    new ValueUInt(ushort.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_UDINT =>
                    new ValueUDInt(uint.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_LINT =>
                    new ValueLInt(long.Parse(value, CultureInfo.InvariantCulture)),
                Softdatatype.S7COMMP_SOFTDATATYPE_ULINT =>
                    new ValueULInt(ulong.Parse(value, CultureInfo.InvariantCulture)),
                // STRING / WSTRING 已在上方按 PlcTag 正确编码，这里不再处理。
                // 不再用 ValueReal 兜底：类型不符的写入 PLC 会静默丢弃，甚至可能误写到相邻变量。
                // 遇到未支持的类型直接报错，由 catch 记录，避免“假成功”。
                _ => throw new NotSupportedException(
                    $"Unsupported write data type: {Softdatatype.Types.GetValueOrDefault(softDt, softDt.ToString(CultureInfo.InvariantCulture))}")
            };
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(ex, "S7PlusClient: cannot write {Symbol}={Value}", symbol, value);
            }
            return null;
        }
    }

    // 匹配西门子时长字面量的 <数字><单位> 段；单位按 2 字符优先(ns/us/ms)再 1 字符(s/m/h/d)排列，避免 "ms" 被误当作 "m"。
    [GeneratedRegex(@"(\d+)\s*(ns|us|ms|s|m|h|d)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex DurationTokenRegex { get; }

    // 解析 "yyyy-MM-dd[ HH:mm:ss[.fff]]" 等常见格式；失败抛 FormatException 由上层 catch 记录并跳过该项。
    private static DateTime ParseDateTimeInvariant(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : throw new FormatException($"Cannot parse date/time value: '{value}'");

    // 解析 "HH:mm:ss[.frac]" 为自午夜起的纳秒数；小数部分右补零到 9 位(纳秒)，超出截断。
    private static long ParseTimeOfDayNs(string value)
    {
        var parts = value.Trim().Split(':');
        if (parts.Length != 3)
        {
            throw new FormatException($"Cannot parse time-of-day value: '{value}'");
        }
        var h = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var m = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var secParts = parts[2].Split('.');
        var sec = int.Parse(secParts[0], CultureInfo.InvariantCulture);
        var fracNs = 0L;
        if (secParts.Length == 2 && secParts[1].Length > 0)
        {
            var frac = secParts[1].Length >= 9 ? secParts[1][..9] : secParts[1].PadRight(9, '0');
            fracNs = long.Parse(frac, CultureInfo.InvariantCulture);
        }
        if (h is < 0 or > 23 || m is < 0 or > 59 || sec is < 0 or > 59)
        {
            throw new FormatException($"Time-of-day out of range: '{value}'");
        }
        return ((h * 3600L) + (m * 60L) + sec) * 1_000_000_000L + fracNs;
    }

    // 解析带符号纳秒时长：纯整数按 <paramref name="bareUnitNs" /> 单位解释；否则按西门子字面量累加各单位段。
    private static long ParseDurationNs(string value, long bareUnitNs)
    {
        var s = value.Trim();
        foreach (var prefix in new[] { "S5TIME#", "S5T#", "LTIME#", "LT#", "TIME#", "T#" })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..];
                break;
            }
        }
        var negative = s.StartsWith('-');
        if (negative || s.StartsWith('+'))
        {
            s = s[1..];
        }
        s = s.Trim();
        if (s.Length == 0)
        {
            throw new FormatException($"Cannot parse duration value: '{value}'");
        }
        long total;
        if (s.All(char.IsDigit))
        {
            total = long.Parse(s, CultureInfo.InvariantCulture) * bareUnitNs;
        }
        else
        {
            var matches = DurationTokenRegex.Matches(s);
            // 校验无未识别残留（去掉所有单位段与分隔符 '_' 后应为空），避免静默接受 "1s5x" 这类脏输入
            if (matches.Count == 0 || DurationTokenRegex.Replace(s, "").Replace("_", "").Trim().Length != 0)
            {
                throw new FormatException($"Cannot parse duration value: '{value}'");
            }
            total = 0;
            foreach (Match match in matches)
            {
                total += long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * UnitToNs(match.Groups[2].Value);
            }
        }
        return negative ? -total : total;
    }

    private static long UnitToNs(string unit) =>
        unit.ToLowerInvariant() switch
        {
            "d" => 86_400_000_000_000L,
            "h" => 3_600_000_000_000L,
            "m" => 60_000_000_000L,
            "s" => 1_000_000_000L,
            "ms" => 1_000_000L,
            "us" => 1_000L,
            "ns" => 1L,
            _ => throw new FormatException($"Unknown time unit: {unit}")
        };

    // 毫秒 → S5Time(时基, 时值)：选最小时基使时值 ≤ 999。时基 0=10ms,1=100ms,2=1s,3=10s。
    private static void SetS5Time(PlcTagS5Time tag, long ms)
    {
        if (ms < 0)
        {
            throw new FormatException("S5Time cannot be negative");
        }
        int[] baseMs = [10, 100, 1000, 10000];
        for (var b = 0; b < baseMs.Length; b++)
        {
            var val = ms / baseMs[b];
            if (val <= 999)
            {
                tag.TimeBase = (ushort)b;
                tag.TimeValue = (ushort)val;
                return;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(ms), "S5Time exceeds max of 9990s");
    }

    #endregion

    #region dispose

    /// <inheritdoc />
    public override string ToString() => $"[S7PlusClient {host}]";

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        // 取锁后优雅断开，确保不与在途 I/O 竞争（全异步，无 Task.Run）
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            isConnected = false;
            var conn = Interlocked.Exchange(ref connection, null);
            if (conn is not null)
            {
                try { await conn.DisconnectAsync().ConfigureAwait(false); } catch { /* 优雅关闭尽力而为 */ }
                try { await conn.DisposeAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }
            resolvedCache.Clear();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    #endregion
}
