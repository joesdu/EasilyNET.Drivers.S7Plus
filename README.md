# EasilyNET.Drivers.S7Plus

通用的西门子 **S7-1200 / S7-1500** 符号寻址采集驱动，基于 **S7CommPlus** 协议，支持 TLS 1.3 加密通信与符号（变量名）读写。纯托管实现，**不依赖原生 OpenSSL DLL**，Windows / Linux 行为一致。

A general-purpose communication driver for Siemens **S7-1200 / S7-1500** PLCs over the **S7CommPlus** protocol. Symbolic (tag-name) read/write with fully-managed TLS 1.3 — no native OpenSSL dependency.

> 本库由 [DeepLogic 产品中的专用驱动](https://github.com/thomas-v2/S7CommPlusDriver) 抽离、去除产品耦合后形成的通用版本：协议层保持不变，仅将高层接口改造为框架无关的 `S7PlusClient`，日志改为 `Microsoft.Extensions.Logging` 抽象。

---

## 1. 概述与适用范围

- **支持型号**：S7-1200、S7-1500（含固件 V2.0 及以上、启用了优化访问的设备）。
- **寻址方式**：**符号寻址**。地址直接填 PLC 中的变量名（如 `DataBlock_1.Temperature`），无需 rack/slot、无需手工计算 DB 偏移量。
- **加密通信**：S7CommPlus 在 IsoOnTCP 之上叠加 **TLS 1.3**。TLS 层使用纯托管的 **BouncyCastle**（`Org.BouncyCastle.Cryptography`），支持 S7CommPlus 合法化所需的 RFC 5705 密钥导出。
- **端口**：固定使用 ISO-on-TCP 端口 **102**（无需也无法另指定端口）。
- **目标框架**：`net10.0` / `net11.0`。

> ⚠️ **不支持**：标准（非优化）数据块的偏移量寻址、S7-300/S7-400、S7-200/S7-200 Smart。

---

## 2. PLC 端前置配置（重要）

在 TIA Portal 中必须完成以下设置，否则连接会在 TLS 或合法化阶段失败：

1. **启用安全通信**：CPU 属性 → “防护与安全” → “连接机制”，确认启用 **安全的 PG/PC 及 HMI 通信**（S7CommPlus 走的是这条加密通道）。
2. **数据块使用优化访问**：需要采集的 DB 属性中勾选 **“优化的块访问”**（默认即为优化）。本驱动只能读取优化 DB。
3. **访问级别 / 密码**：
   - 设为 **完全访问（无保护）** 时，用户名/密码可留空。
   - 若设置了 HMI/读写访问密码，则需在选项中填入对应**用户名与密码**（通过 OMS 合法化完成认证）。
4. **下载到 PLC** 后配置才生效。

---

## 3. 安装

```bash
dotnet add package EasilyNET.Drivers.S7Plus
```

---

## 4. 快速开始

本客户端为**异步优先**（async-first）：连接/读/写/断开均为 `Task` 异步方法，并支持 `CancellationToken`。

```csharp
using EasilyNET.Drivers.S7Plus;

// logger 可选；传 null 则不输出日志。推荐 await using 以异步释放
await using var client = new S7PlusClient(
    host: "192.168.0.1",
    options: new S7PlusClientOptions
    {
        Username = "",       // 无密码保护时留空
        Password = "",
        TimeoutMs = 5000
    },
    logger: null);

// 连接（异步）。Read/Write 在未连接时也会自动尝试连接。
if (!await client.ConnectAsync())
{
    Console.WriteLine("连接失败");
    return;
}

// 批量读取：按 PLC 中每个符号的真实类型解码为字符串
foreach (var v in await client.ReadAsync("DB_1.Temperature", "DB_1.Motor.Speed", "DB_1.Buffer[5]"))
{
    Console.WriteLine($"{v.Symbol} = {(v.IsGood ? v.Value : "<bad>")} @ {v.Timestamp}");
}

// 写入：按解析到的真实类型编码
await client.WriteAsync("DB_1.Setpoint", "42");
await client.WriteAsync(new Dictionary<string, string>
{
    ["DB_1.Enable"] = "1",
    ["DB_1.Name"]   = "Hello"
});

// 带取消令牌
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
var values = await client.ReadAsync(["DB_1.Temperature"], cts.Token);
```

### 公开 API

| 成员 | 说明 |
|---|---|
| `new S7PlusClient(host, options?, logger?)` | 创建客户端；`options` 含用户名/密码/超时，`logger` 为可选 `ILogger` |
| `Task<bool> ConnectAsync(ct = default)` | 异步连接，已连接返回 `true` |
| `Task DisconnectAsync(ct = default)` | 异步断开并释放底层连接 |
| `bool Connected` | 当前是否已连接 |
| `Task<IReadOnlyList<S7TagValue>> ReadAsync(params string[] symbols)` | 异步批量读取；未连接时自动连接 |
| `Task<IReadOnlyList<S7TagValue>> ReadAsync(IEnumerable<string> symbols, ct = default)` | 同上，带取消令牌 |
| `Task<bool> WriteAsync(string symbol, string value, ct = default)` | 异步写单点 |
| `Task<bool> WriteAsync(IEnumerable<KeyValuePair<string,string>> writes, ct = default)` | 异步批量写 |
| `ValueTask DisposeAsync()` | 异步释放（仅 `IAsyncDisposable`，请用 `await using`） |

`S7TagValue`：`Symbol`（符号）、`Value`（解码后的字符串，失败为 `null`）、`IsGood`（是否成功）、`Timestamp`（读取时间）。

> **关于异步的说明**：本驱动为**自上而下的真异步实现**——从底层 socket 收发（`Socket.*Async`）、TLS 密文泵、ISO 帧组装，到 S7CommPlus 请求-响应等待，全链路均为真正的 `await`，**无后台线程、无忙等待轮询、无 `Task.Run` 包装**。接收由单个异步“接收泵”驱动，响应经异步信号量交付，`CancellationToken` 贯穿全程。所有 I/O 通过同一异步信号量串行化，可从多处并发 `await`（自动排队），避免同一连接上的并发请求导致协议序列号错乱。

---

## 5. 点表地址（变量名）格式

地址 = PLC 中的符号路径。驱动在首次读/写该点时按需解析为内部访问序列并缓存。

```
<块名>.<成员名>[.<子成员名>...]            普通数据块 / 结构体 / UDT 成员
<块名>.<数组名>[<下标>]                    一维数组元素
<块名>.<数组名>[<下标1>,<下标2>,...]       多维数组元素
"<含特殊字符的块名>"."<含特殊字符的成员>"   名称含空格/点号时用英文双引号包裹该层
```

| 类型 | 示例 |
|---|---|
| 数据块成员 | `DB_1.Temperature`、`DB_1.Speed` |
| 结构体 / UDT 嵌套 | `DB_1.Motor.Current` |
| 含特殊字符的名称 | `"My DB"."My Tag"` |
| 一维 / 多维数组元素 | `DB_1.Buffer[5]`、`DB_1.Matrix[2,3]` |
| 功能块实例 DB 成员 | `FB1_DB.Speed` |
| 工艺对象成员 | `PID_Compact_1.Setpoint` |

> 名称需与 TIA Portal 中完全一致。解析失败会在日志中提示“not found in PLC”。

### 兼容上位机/组态软件的完整路径（自动剥离前缀）

很多组态软件（如 Kepware）导出的地址带 **设备名 + 标签组** 前缀，例如 `PLC_1.Blocks.DB1.test1`。驱动会**自动逐级剥离前缀**后再解析：先用完整地址尝试，PLC 不识别就去掉最前面一段重试，直到命中。因此可直接填写完整路径，也可填原生地址（`DB1.test1`）。

- 不依赖前缀固定为 `PLC_1` / `Blocks`——通道名、标签组名任意都可。
- 块名含特殊字符需用双引号时（`"My DB".tag`），从引号处起视为块名，不再剥离。

### 数据类型（自动识别，无需配置）

驱动在解析符号时从 PLC 的类型信息中获取每个变量的真实类型，读取按真实类型解码，写入按真实类型编码。支持：Bool、Byte/USInt/SInt、Word/Int/UInt、DWord/DInt/UDInt、LWord/LInt/ULInt、Real、LReal、Char/WChar、String/WString、Date、Time/LTime、TimeOfDay/LTOD、DateAndTime/DTL/LDT 及上述类型的数组。

### 读取取值输出格式

| 类别 | 输出形式 | 示例 |
|---|---|---|
| 布尔 Bool | `1` / `0` | `1` |
| 整数 | 十进制 | `1231` |
| 浮点 Real / LReal | 小数点固定为 `.`（不受区域影响） | `3.14` |
| 字符串 / 字符 | 纯文本 | `Hello` / `A` |
| 日期/日期时间 Date、DateAndTime、DTL、LDT | `yyyy-MM-dd HH:mm:ss` | `2026-06-23 12:34:56` |
| 时刻 TimeOfDay / LTOD | `HH:mm:ss` | `12:34:56` |
| 时长 Time / LTime / S5Time | 可读时长 | `1d_2h_30m` |
| 数组 | 元素以英文逗号拼接 | `10,20,30` |

---

## 6. 写值说明（重要）

**可写类型**：Bool、Byte、USInt、SInt、Char、WChar、Word、Int、UInt、DWord、DInt、UDInt、LWord、LInt、ULInt、Real、LReal、String、WString。

| 类型 | 写入字符串 |
|---|---|
| Bool | `1` / `0` / `true`（其它视为 `false`） |
| 整数 | 十进制，如 `1231`、`-5`（超范围写入失败并记录日志） |
| Real / LReal | `3.14`（小数点用 `.`） |
| Char / WChar | 单字符，取首字符 |
| String / WString | 纯文本 |

**暂不支持写入**（会记录“Unsupported write data type”并跳过，不会误写）：Date、Time、S5Time、TimeOfDay、DateAndTime、DTL、LTime、LTOD、LDT、Pointer、Any 以及**所有数组类型**。

> **哪些点“写了不生效”**：由 PLC 程序/系统逻辑每周期覆盖的变量（如定时器实例的输出位 `.Q`、功能块实例的输出参数 `FB_DB.OUT`），与上位机 RO/RW 标记无关——`WriteValues` 返回 `0` 但值很快被覆盖；要外部控制应改写其**输入参数**或预设值而非输出。

---

## 7. 运行机制与可靠性

- **按需解析**：连接成功后**不会**全量浏览整个 PLC，仅解析实际用到的符号，显著缩短大型程序的连接准备时间。
- **批量读取**：多个点按 PLC 协商的单请求最大点数自动分块。
- **自动重连**：通信异常 / 读写超时 / PLC 主动终止会话时，`Read`/`Write` 会断开连接；下次调用自动重连并重新解析符号。
- **资源释放**：断开/重连及 `Dispose` 时完整释放底层连接（`S7Client`/Socket/Mutex 等）。
- **线程安全 / 并发**：全部 I/O（连接/读/写/断开）通过同一异步信号量串行化，可安全地从多处并发 `await`——调用会自动排队，无需调用方手动串行化（避免同一连接的并发请求导致序列号错乱）。

---

## 8. 技术说明与许可

- 协议实现基于开源项目 **[thomas-v2/S7CommPlusDriver](https://github.com/thomas-v2/S7CommPlusDriver)**（**LGPL-3.0**），并做了以下增强：
  - TLS 层由原生 OpenSSL P/Invoke 迁移为纯托管 **BouncyCastle**（TLS 1.3 + AES-GCM，支持 RFC 5705 `EXPERIMENTAL_OMS` 密钥导出）。
  - 连接层加入事务锁、按需符号解析（移除全量浏览）、自动剥离上位机路径前缀。
  - 取值按真实类型解码、写值按真实类型编码、完善资源释放。
### 许可（双许可 Dual License）

本仓库按组件分别授权，每个源文件以 `SPDX-License-Identifier` 头声明其许可：

| 组件 | 许可 | 范围 |
|---|---|---|
| 高层封装 | **MIT** | `S7PlusClient.cs`、`S7PlusClientOptions.cs`、`S7TagValue.cs` |
| S7CommPlus 协议层 | **LGPL-3.0-or-later** | `src/EasilyNET.Drivers.S7Plus/S7CommPlus/` 全部文件（派生自 [thomas-v2/S7CommPlusDriver](https://github.com/thomas-v2/S7CommPlusDriver)，© 2023 Thomas Wiens） |

- 许可全文：根目录 [`LICENSE`](LICENSE)（声明 + MIT）、[`LICENSE-LGPL-3.0.txt`](LICENSE-LGPL-3.0.txt)、[`LICENSE-GPL-3.0.txt`](LICENSE-GPL-3.0.txt)。
- 因协议层为 LGPL-3.0，编译后的整库（`EasilyNET.Drivers.S7Plus.dll`）须按 LGPL-3.0 条款使用与再分发（接收方有权对 LGPL 部分重新链接/替换）。MIT 仅覆盖上述三个封装文件，不削弱协议层的 LGPL 义务。**以 NuGet 包形式动态引用（动态链接）本库时**，保留这些声明并向用户提供对应源码即可满足 LGPL。
- 之所以不用 .NET 内置 `SslStream`：其至今不支持导出 TLS 密钥材料（RFC 5705），而 S7CommPlus 合法化必须依赖该能力。
