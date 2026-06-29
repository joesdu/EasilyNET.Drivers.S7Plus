// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Joe Du. See LICENSE.
namespace EasilyNET.Drivers.S7Plus;

/// <summary>
///     一次读取的结果项。
///     <para xml:lang="en">A single read result item.</para>
/// </summary>
/// <param name="Symbol">读取所用的 PLC 符号（点表地址）。<para xml:lang="en">The PLC symbol (point address) that was read.</para></param>
/// <param name="Value">
///     按真实数据类型解码后的字符串值；解析失败、质量不良或读取出错时为 <see langword="null" />。
///     <para xml:lang="en">String value decoded by the tag's real data type; <see langword="null" /> when the symbol could not be resolved, the quality is bad, or the read failed.</para>
/// </param>
/// <param name="IsGood">该点是否成功读取到有效值。<para xml:lang="en">Whether a valid value was read for this symbol.</para></param>
/// <param name="Timestamp">读取完成的本地时间戳。<para xml:lang="en">Local timestamp when the read completed.</para></param>
public readonly record struct S7TagValue(string Symbol, string? Value, bool IsGood, DateTime Timestamp);
