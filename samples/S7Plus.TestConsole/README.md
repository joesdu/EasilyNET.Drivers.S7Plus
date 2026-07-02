# S7Plus.TestConsole

`EasilyNET.Drivers.S7Plus` 的**交互式 TUI 测试台**：以实时表格持续采集西门子 PLC 的符号点位，并支持在终端里直接选点、输入值、按 Enter 下发。基于 [Spectre.Console](https://spectreconsole.net/) 就地刷新（ANSI），无需滚屏。

An interactive TUI sample for `EasilyNET.Drivers.S7Plus`: live-updating table of PLC symbols with in-terminal write (select a tag → type a value → Enter to write).

> 仅为示例项目，`IsPackable=false`，不随库发布。目标框架 `net11.0`。

---

## 1. 运行

在仓库根目录执行：

```bash
# 实时表格（默认连接 192.168.10.12）
dotnet run --project samples/S7Plus.TestConsole

# 覆盖 PLC IP
dotnet run --project samples/S7Plus.TestConsole -- 192.168.10.13

# 只读一次后退出（适合快速验证 / 脚本）
dotnet run --project samples/S7Plus.TestConsole --once

# 滚动模式并输出驱动 Debug 日志（排查连接/符号解析）
dotnet run --project samples/S7Plus.TestConsole --debug

# 自定义轮询间隔（毫秒）
dotnet run --project samples/S7Plus.TestConsole -- 192.168.10.12 --interval 1000
```

### 命令行参数

| 参数 | 说明 |
|---|---|
| `-- <host>` | 覆盖默认 PLC IP（位置参数） |
| `--debug` | 改为滚动模式并输出驱动 Debug 日志 |
| `--once` | 只读取一次后退出 |
| `--interval <ms>` | 轮询间隔毫秒，默认 `500` |

### 连接与点位配置

打开 `Program.cs` 顶部按需修改：

- `host` / `username` / `password`：目标 IP 与访问凭据（无密码保护时用户名/密码留空）。
- `tags`：待测点位表 `(Tag 名, PLC 符号地址, 类型, 读写权限)`。默认内置 8 个测试点，地址用**完整路径**（含 `PLC_1.Blocks` 前缀），驱动会自动逐级剥离前缀。

```csharp
var tags = new (string Name, string Address, string Type, string Access)[]
{
    ("TEST1", "PLC_1.Blocks.DB1.test1", "Word", "R/W"),
    ("TEST4", "PLC_1.Blocks.DB1.test4", "Real", "R/W"),
    // ...
};
```

---

## 2. 预期效果

### 启动与连接

```
S7Plus 测试台 / test console
目标 PLC / target: 192.168.10.12    点位 / tags: 8    间隔 / interval: 500ms
连接中 / connecting ...
已连接 / connected。 交互：1-8 写入对应行 · w 选择写入 · q 退出。
```

### 实时表格（每 500ms 就地刷新，不滚屏）

```
                             S7Plus  192.168.10.12
╭───┬───────┬───────┬─────┬─────────┬────────┬───┬─────────────────────────────╮
│ # │ Tag   │ Type  │ Acc │ Quality │ Value  │ Δ │ Address                     │
├───┼───────┼───────┼─────┼─────────┼────────┼───┼─────────────────────────────┤
│ 1 │ TEST1 │ Word  │ R/W │ GOOD    │ 1235   │ 8 │ PLC_1.Blocks.DB1.test1      │
│ 2 │ TEST2 │ Int   │ RO  │ GOOD    │ -42    │ 2 │ PLC_1.Blocks.DB1.test2      │
│ 3 │ TEST3 │ DWord │ R/W │ GOOD    │ 100000 │ 0 │ PLC_1.Blocks.DB1.test3      │
│ 4 │ TEST4 │ Real  │ R/W │ GOOD    │ 3.14   │ 5 │ PLC_1.Blocks.DB1.test4      │
│ 5 │ TEST5 │ Bool  │ R/W │ GOOD    │ 1      │ 3 │ PLC_1.Blocks.DB1.test5      │
│ 6 │ TEST6 │ Word  │ RO  │ GOOD    │ 500    │ 1 │ PLC_1.Blocks.FB1_DB.OUT     │
│ 7 │ TEST7 │ Real  │ R/W │ GOOD    │ 25.5   │ 4 │ PLC_1.Blocks.PID_Compact_1. │
│   │       │       │     │         │        │   │ Setpoint                    │
│ 8 │ TEST8 │ Bool  │ RO  │ GOOD    │ 0      │ 9 │ PLC_1.Blocks.IEC_Timer_0_DB │
│   │       │       │     │         │        │   │ .Q                          │
╰───┴───────┴───────┴─────┴─────────┴────────┴───┴─────────────────────────────╯
     poll #128 · 10:42:07 · [1-8] 写入 · [w] 选择 · [q] 退出
```

真实终端里带颜色：

| 列 | 颜色规则 |
|---|---|
| `Quality` | GOOD 绿 · BAD 红 · UNRESOLVED 黄 · NO-CONN 红 |
| `Acc` | R/W 绿 · RO 灰 |
| `Value` | 常态白；**某点值变化的那一轮以亮黄加粗闪现** |
| `Δ` | 该点累计变化次数 |

- `#` 列为行号，对应下面的写入热键。
- 脚注还会显示上次下发结果（如 `✓ 写入成功 TEST4 = 42.5`）。

### 交互式下发

按 `w` 弹出选择列表（方向键选、Enter 确认）：

```
选择要写入的点位 / select a tag to write
> 1. TEST1  (Word, R/W)  = 1235
  4. TEST4  (Real, R/W)  = 3.14
  ...
  ↩ 取消 / cancel
```

选中后弹出输入框，填值 → **Enter 即下发**：

```
下发 / write → TEST4  (Real, R/W)
当前值 / current: 3.14    小数点用 .，如 3.14
输入新值 / new value: 42.5▮       ← 输入后按 Enter 触发
✓ 写入成功 / OK  TEST4 = 42.5
按任意键返回实时表格 / press any key to return ...
```

下一轮轮询即可看到该点变为新值并高亮。写只读点或类型不符时显示红色 `✗ 写入失败`（加 `--debug` 可查看驱动日志定位原因）。

---

## 3. 交互键

| 键 | 动作 |
|---|---|
| `1`–`8` | 立即下发对应行的点位（跳过选择列表） |
| `w` | 弹出选择列表后下发 |
| `q` / `Esc` / `Ctrl+C` | 退出 |

> 交互仅在**实时模式**下可用；`--debug`、`--once` 以及输出/输入被重定向（管道/CI）时自动退回**滚动模式**（无交互，每轮打印一张新表），以避免 Live 独占控制台与日志/无控制台句柄冲突。

---

## 4. 状态含义速查

| Quality | 含义 | 处理建议 |
|---|---|---|
| `GOOD` | 读到有效值 | — |
| `BAD` | 命中但质量不良（如该点读取报错） | 检查点位是否可访问 |
| `UNRESOLVED` | 符号名未在 PLC 中解析成功 | 核对 `tags` 里的地址是否与 TIA 中一致 |
| `NO-CONN` | 本轮整批通信失败（已断开） | 驱动会自动重连，观察下一轮；持续失败加 `--debug` |

---

## 5. 依赖与说明

- 引用本仓库的 `EasilyNET.Drivers.S7Plus` 项目（`ProjectReference`）。
- [`Spectre.Console`](https://www.nuget.org/packages/Spectre.Console) 提供 TUI 渲染；`Microsoft.Extensions.Logging.Console` 用于 `--debug` 日志。
- PLC 端前置配置（安全通信、优化访问、访问级别/密码）与地址格式，详见[库根目录 README](../../README.md)。
