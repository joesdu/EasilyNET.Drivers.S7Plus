// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Joe Du. See LICENSE.
using EasilyNET.Drivers.S7Plus.S7CommPlus;
using EasilyNET.Drivers.S7Plus.S7CommPlus.ClientApi;
using EasilyNET.Drivers.S7Plus.S7CommPlus.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Globalization;

namespace EasilyNET.Drivers.S7Plus;

/// <summary>
///     西门子 S7-1200/S7-1500 符号寻址客户端（S7CommPlus 协议，TLS 加密）。
///     地址直接填 PLC 中的符号变量名，如 <c>"DataBlock_1.Temperature"</c>；
///     需在 TIA Portal 中开启“安全的 PG/PC 及 HMI 通信”，且数据块为优化访问。
///     <para xml:lang="en">
///         Symbolic-addressing client for Siemens S7-1200/S7-1500 over the S7CommPlus protocol (TLS encrypted).
///         Addresses are PLC symbol names such as <c>"DataBlock_1.Temperature"</c>; the PLC must have
///         "secure PG/PC and HMI communication" enabled and the data blocks must use optimized access.
///     </para>
/// </summary>
/// <remarks>
///     本类型非线程安全的并发读写——读与写应串行调用（内部对连接生命周期加锁，但不为并发 IO 提供事务串行化）。
///     <para xml:lang="en">This type is not safe for concurrent reads/writes; serialize read and write calls.</para>
/// </remarks>
public sealed class S7PlusClient : IDisposable
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";
    private readonly string host;
    private readonly string username;
    private readonly string password;
    private readonly int timeoutMs;
    private readonly ILogger logger;
    private readonly Lock sync = new();

    // 点表地址(符号) → 已解析的 PlcTag（含 ItemAddress 与 Softdatatype）。
    // 按需懒解析，断开时清空；避免连接后对整个 PLC 做全量 Browse。
    // 值为 null 表示该符号在当前连接中解析失败（未找到），本连接周期内不再重复解析。
    private readonly ConcurrentDictionary<string, PlcTag?> resolvedCache = new(StringComparer.OrdinalIgnoreCase);
    private S7CommPlusConnection? connection;
    private bool isConnected;
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
    ///     连接到 PLC（同步阻塞）。已连接时直接返回 <see langword="true" />。
    ///     <para xml:lang="en">Connects to the PLC (synchronous, blocking). Returns <see langword="true" /> immediately if already connected.</para>
    /// </summary>
    /// <returns>连接成功返回 <see langword="true" />。<para xml:lang="en"><see langword="true" /> on success.</para></returns>
    public bool Connect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (sync)
        {
            if (isConnected)
            {
                return true;
            }
            try
            {
                var conn = new S7CommPlusConnection(logger);
                var res = conn.Connect(host, password, username, timeoutMs);
                if (res == 0)
                {
                    connection = conn;
                    isConnected = true;
                    resolvedCache.Clear();
                    logger.LogDebug("S7PlusClient: connected to {Host}", host);
                    return true;
                }
                logger.LogDebug("S7PlusClient: connect to {Host} failed, res=0x{Res:X}", host, res);
                try { conn.Disconnect(); } catch { /* ignore */ }
                try { conn.Dispose(); } catch { /* ignore */ }
                return false;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "S7PlusClient: connect to {Host} error", host);
                DisconnectCore();
                return false;
            }
        }
    }

    /// <summary>
    ///     断开与 PLC 的连接（协议层优雅关闭并释放底层资源）。
    ///     <para xml:lang="en">Disconnects from the PLC (graceful protocol shutdown and resource release).</para>
    /// </summary>
    public void Disconnect()
    {
        lock (sync)
        {
            DisconnectCore();
        }
    }

    private void DisconnectCore()
    {
        isConnected = false;
        // 先把字段置空，避免其它线程在释放过程中再拿到正在销毁的连接
        var conn = Interlocked.Exchange(ref connection, null);
        if (conn is not null)
        {
            try { conn.Disconnect(); } catch { /* 协议层优雅关闭：删除会话、关 socket、停接收线程 */ }
            try { conn.Dispose(); } catch { /* 释放 S7Client/Socket/Mutex，避免重连泄漏句柄 */ }
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
    private PlcTag? Resolve(string address)
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
            tag = ResolvePlcTag(conn, address);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "S7PlusClient: resolve symbol ({Address}) error", address);
        }
        if (tag is null)
        {
            logger.LogDebug("S7PlusClient: symbol ({Address}) not found in PLC", address);
        }
        // 缓存成功或失败结果（断开时清空），避免每个读取周期重复解析同一符号
        resolvedCache[address] = tag;
        return tag;
    }

    private static PlcTag? ResolvePlcTag(S7CommPlusConnection conn, string address)
    {
        var symbol = address;
        while (true)
        {
            var tag = conn.GetPlcTagBySymbol(symbol);
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
    ///     批量读取多个符号；未连接时会自动尝试连接。
    ///     <para xml:lang="en">Reads multiple symbols in one batch; automatically connects if not yet connected.</para>
    /// </summary>
    /// <param name="symbols">PLC 符号列表。<para xml:lang="en">The PLC symbols to read.</para></param>
    /// <returns>与输入顺序一致的读取结果。<para xml:lang="en">Read results in the same order as the input.</para></returns>
    public IReadOnlyList<S7TagValue> Read(params string[] symbols) => Read((IEnumerable<string>)symbols);

    /// <inheritdoc cref="Read(string[])" />
    public IReadOnlyList<S7TagValue> Read(IEnumerable<string> symbols)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(symbols);
        var list = symbols as IList<string> ?? [.. symbols];
        if (list.Count == 0)
        {
            return [];
        }
        if (!Connected && !Connect())
        {
            return [];
        }
        try
        {
            return ReadCore(list);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "S7PlusClient: read error");
            return [];
        }
    }

    private IReadOnlyList<S7TagValue> ReadCore(IList<string> symbols)
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
            var resolved = Resolve(symbol);
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
        var res = conn.ReadValues(addressList, out var values, out var errors);

        // 任意非零返回都视为通信/会话异常（如 PLC 主动终止会话、读超时）：
        // 断开以触发下次重连，而不是返回一堆 null 并永久卡死。
        if (res != 0)
        {
            logger.LogDebug("S7PlusClient: ReadValues res=0x{Res:X}", res);
            Disconnect();
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
                strValue = FormatValue(tagList[i], values[i]);
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
                or PlcTagPointer or PlcTagAny:
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
                    // 时长/时刻类(Time/LTime/S5Time/TimeOfDay/LTOD)与指针等：保留可读格式，去质量前缀
                    _ => StripQuality(tag.ToString())
                };
            default:
                break;
        }

        // ② 数组：直接从协议值取底层数组并逗号拼接（PlcTag/PValue 数组的 ToString 是 XML，不可用）
        var arr = FormatArray(value, ic);
        if (arr is not null)
        {
            return arr;
        }

        // ③ 普通标量：布尔统一 "1"/"0"，浮点用不变区域避免逗号小数点
        return FormatScalar(value, ic);
    }

    private static string? FormatScalar(object value, CultureInfo ic) =>
        value switch
        {
            ValueBool v => v.GetValue() ? "1" : "0",
            ValueByte v => v.GetValue().ToString(ic),
            ValueUSInt v => v.GetValue().ToString(ic),
            ValueSInt v => v.GetValue().ToString(ic),
            ValueUInt v => v.GetValue().ToString(ic),
            ValueInt v => v.GetValue().ToString(ic),
            ValueWord v => v.GetValue().ToString(ic),
            ValueUDInt v => v.GetValue().ToString(ic),
            ValueDInt v => v.GetValue().ToString(ic),
            ValueDWord v => v.GetValue().ToString(ic),
            ValueULInt v => v.GetValue().ToString(ic),
            ValueLInt v => v.GetValue().ToString(ic),
            ValueLWord v => v.GetValue().ToString(ic),
            ValueReal v => v.GetValue().ToString(ic),
            ValueLReal v => v.GetValue().ToString(ic),
            ValueTimestamp v => v.GetValue().ToString(ic),
            ValueTimespan v => v.GetValue().ToString(ic),
            ValueRID v => v.GetValue().ToString(ic),
            _ => value.ToString()
        };

    private static string? FormatArray(object value, CultureInfo ic) =>
        value switch
        {
            ValueBoolArray a => string.Join(",", a.GetValue().Select(b => b ? "1" : "0")),
            ValueByteArray a => string.Join(",", a.GetValue()),
            ValueUSIntArray a => string.Join(",", a.GetValue()),
            ValueSIntArray a => string.Join(",", a.GetValue()),
            ValueWordArray a => string.Join(",", a.GetValue()),
            ValueUIntArray a => string.Join(",", a.GetValue()),
            ValueIntArray a => string.Join(",", a.GetValue()),
            ValueDWordArray a => string.Join(",", a.GetValue()),
            ValueUDIntArray a => string.Join(",", a.GetValue()),
            ValueDIntArray a => string.Join(",", a.GetValue()),
            ValueLWordArray a => string.Join(",", a.GetValue()),
            ValueULIntArray a => string.Join(",", a.GetValue()),
            ValueLIntArray a => string.Join(",", a.GetValue()),
            ValueRealArray a => string.Join(",", a.GetValue().Select(x => x.ToString(ic))),
            ValueLRealArray a => string.Join(",", a.GetValue().Select(x => x.ToString(ic))),
            ValueTimestampArray a => string.Join(",", a.GetValue()),
            ValueTimespanArray a => string.Join(",", a.GetValue()),
            _ => null
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
    ///     写入单个符号的值（按 PLC 中该符号的真实数据类型编码）。
    ///     <para xml:lang="en">Writes a single symbol (encoded by the symbol's real data type in the PLC).</para>
    /// </summary>
    /// <param name="symbol">PLC 符号。<para xml:lang="en">The PLC symbol.</para></param>
    /// <param name="value">字符串形式的写入值。<para xml:lang="en">The value to write, as a string.</para></param>
    /// <returns>写入是否成功提交。<para xml:lang="en">Whether the write was committed successfully.</para></returns>
    public bool Write(string symbol, string value) =>
        Write([new(symbol, value)]);

    /// <summary>
    ///     批量写入多个符号。
    ///     <para xml:lang="en">Writes multiple symbols in one batch.</para>
    /// </summary>
    /// <param name="writes">符号到写入值的映射。<para xml:lang="en">Mapping from symbol to value.</para></param>
    /// <returns>写入是否成功提交。<para xml:lang="en">Whether the write was committed successfully.</para></returns>
    public bool Write(IEnumerable<KeyValuePair<string, string>> writes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(writes);
        var items = writes as IList<KeyValuePair<string, string>> ?? [.. writes];
        if (items.Count == 0)
        {
            return false;
        }
        if (!Connected && !Connect())
        {
            return false;
        }
        var conn = connection;
        if (conn is null)
        {
            return false;
        }
        var addressList = new List<ItemAddress>(items.Count);
        var valueList = new List<PValue>(items.Count);
        foreach (var item in items)
        {
            var resolved = Resolve(item.Key);
            if (resolved is null)
            {
                logger.LogDebug("S7PlusClient: cannot write {Symbol}={Value}, address not resolved", item.Key, item.Value);
                continue;
            }
            // 使用解析得到的实际数据类型，避免误当作 REAL 处理
            var pval = CreateWriteValue(item.Key, item.Value, resolved.Datatype);
            if (pval is not null)
            {
                addressList.Add(resolved.Address);
                valueList.Add(pval);
            }
        }
        if (addressList.Count == 0)
        {
            return false;
        }
        try
        {
            var res = conn.WriteValues(addressList, valueList, out _);
            if (res != 0)
            {
                logger.LogDebug("S7PlusClient: WriteValues res=0x{Res:X}", res);
                Disconnect();
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "S7PlusClient: write error");
            Disconnect();
            return false;
        }
    }

    private PValue? CreateWriteValue(string symbol, string value, uint softDt)
    {
        try
        {
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
                Softdatatype.S7COMMP_SOFTDATATYPE_STRING =>
                    new ValueWString(value),
                Softdatatype.S7COMMP_SOFTDATATYPE_WSTRING =>
                    new ValueWString(value),
                // 不再用 ValueReal 兜底：类型不符的写入 PLC 会静默丢弃，甚至可能误写到相邻变量。
                // 遇到未支持的类型直接报错，由 catch 记录，避免“假成功”。
                _ => throw new NotSupportedException(
                    $"Unsupported write data type: {Softdatatype.Types.GetValueOrDefault(softDt, softDt.ToString(CultureInfo.InvariantCulture))}")
            };
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "S7PlusClient: cannot write {Symbol}={Value}", symbol, value);
            return null;
        }
    }

    #endregion

    #region dispose

    /// <inheritdoc />
    public override string ToString() => $"[S7PlusClient {host}]";

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Disconnect();
    }

    #endregion
}
