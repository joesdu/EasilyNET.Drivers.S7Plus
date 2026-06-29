// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Joe Du. See LICENSE.
namespace EasilyNET.Drivers.S7Plus;

/// <summary>
///     <see cref="S7PlusClient" /> 的连接选项。S7CommPlus 固定使用 ISO-on-TCP 端口 102，无需也无法另指定端口。
///     <para xml:lang="en">Connection options for <see cref="S7PlusClient" />. S7CommPlus always uses ISO-on-TCP port 102; the port cannot be changed.</para>
/// </summary>
public sealed class S7PlusClientOptions
{
    /// <summary>
    ///     PLC 访问用户名；无密码保护时留空。
    ///     <para xml:lang="en">PLC access user name; leave empty when the PLC is not password protected.</para>
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    ///     PLC 访问密码；无密码保护时留空。
    ///     <para xml:lang="en">PLC access password; leave empty when the PLC is not password protected.</para>
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    ///     建立连接 / 单次请求等待响应的超时（毫秒），缺省 5000，建议 ≥ 5000。
    ///     <para xml:lang="en">Connect / request timeout in milliseconds. Default 5000; values ≥ 5000 are recommended.</para>
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;
}
