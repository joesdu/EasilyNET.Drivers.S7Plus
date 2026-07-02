// S7Plus.TestConsole —— 西门子 PLC 数据读取 + 交互式下发测试台（Spectre.Console TUI）
// S7Plus test console: live-updating table of PLC symbols; press 1-8 (or 'w') to write a tag.
//
// 用法 / Usage:
//   dotnet run --project samples/S7Plus.TestConsole [-- <host>] [--debug] [--once] [--interval <ms>]
//   例 / e.g.:  dotnet run --project samples/S7Plus.TestConsole -- 192.168.10.12
//
//   <host>        可选，覆盖默认 PLC IP / optional, overrides the default PLC IP
//   --debug       改为滚动模式并输出驱动 Debug 日志（诊断连接/解析）/ scrolling mode + driver Debug logs
//   --once        只读取一次后退出 / read once and exit
//   --interval N  轮询间隔毫秒，默认 500 / poll interval in ms (default 500)
//
// 交互 / Interaction（仅实时模式 / live mode only）:
//   1-8   立即写入对应行的点位 / write the tag on that row
//   w     弹出选择列表后写入 / pick a tag from a list, then write
//   q     退出（等同 Ctrl+C）/ quit
using EasilyNET.Drivers.S7Plus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

// ---- 默认连接参数 / default connection parameters ----
var host = "192.168.10.12";
var username = "";  // 无密码保护时留空 / leave empty when the PLC is not password protected
var password = "";

// ---- 待测点位：Tag 名 → PLC 符号地址（来自提供的点表）/ test points: name → PLC symbol ----
// 完整路径（含 PLC_1.Blocks 前缀）可直接使用，驱动会自动逐级剥离前缀。
var tags = new (string Name, string Address, string Type, string Access)[]
{
    ("TEST1", "PLC_1.Blocks.DB1.test1", "Word", "R/W"),
    ("TEST2", "PLC_1.Blocks.DB1.test2", "Int", "RO"),
    ("TEST3", "PLC_1.Blocks.DB1.test3", "DWord", "R/W"),
    ("TEST4", "PLC_1.Blocks.DB1.test4", "Real", "R/W"),
    ("TEST5", "PLC_1.Blocks.DB1.test5", "Bool", "R/W"),
    ("TEST6", "PLC_1.Blocks.FB1_DB.OUT", "Word", "RO"),
    ("TEST7", "PLC_1.Blocks.PID_Compact_1.Setpoint", "Real", "R/W"),
    ("TEST8", "PLC_1.Blocks.IEC_Timer_0_DB.Q", "Bool", "RO")
};

// ---- 解析命令行参数 / parse CLI args ----
var debug = false;
var once = false;
var intervalMs = 500;
for (var i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (a.Equals("--debug", StringComparison.OrdinalIgnoreCase)) { debug = true; }
    else if (a.Equals("--once", StringComparison.OrdinalIgnoreCase)) { once = true; }
    else if (a.Equals("--interval", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[++i], out var ms) && ms > 0) { intervalMs = ms; }
    else if (!a.StartsWith('-')) { host = a; } // 位置参数视为 host / positional arg overrides host
}

// 实时表格（Live）独占控制台且需要键盘交互：仅在“非 --debug、非 --once、输出未重定向、输入未重定向”时启用。
// --debug 走滚动模式并打印日志；输出/输入被重定向（管道/CI）时也退回滚动，避免 Live 隐藏光标/读键异常。
var useLive = !debug && !once && !Console.IsOutputRedirected && !Console.IsInputRedirected;

using var loggerFactory = LoggerFactory.Create(b =>
{
    b.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    b.SetMinimumLevel(LogLevel.Debug);
});
ILogger<S7PlusClient> clientLogger = debug ? loggerFactory.CreateLogger<S7PlusClient>() : NullLogger<S7PlusClient>.Instance;

// ---- Ctrl+C 优雅停止 / graceful stop on Ctrl+C ----
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // 阻止进程被立即杀死，改为协作式取消
    cts.Cancel();
};

await using var client = new S7PlusClient(host, new S7PlusClientOptions
{
    Username = username,
    Password = password,
    TimeoutMs = 5000
}, clientLogger);

AnsiConsole.MarkupLine("[bold]S7Plus 测试台[/] / test console");
AnsiConsole.MarkupLine($"目标 PLC / target: [cyan]{Markup.Escape(host)}[/]    点位 / tags: {tags.Length}    间隔 / interval: {intervalMs}ms");
AnsiConsole.MarkupLine("连接中 / connecting ...");

try
{
    if (!await client.ConnectAsync(cts.Token))
    {
        AnsiConsole.MarkupLine("[red]连接失败 / connection failed[/]。请检查 IP、TIA 中的安全通信设置与访问级别；加 [yellow]--debug[/] 查看详细日志。");
        return 1;
    }
}
catch (OperationCanceledException)
{
    return 0;
}

AnsiConsole.MarkupLine("[green]已连接 / connected[/]。" + (once ? "" : " 交互：[yellow]1-8[/] 写入对应行 · [yellow]w[/] 选择写入 · [yellow]q[/] 退出。"));

var addresses = Array.ConvertAll(tags, t => t.Address);
// 每个点位的上一次值与累计变化次数：用于就地刷新与变化高亮
var prev = new string?[tags.Length];
var changeCount = new int[tags.Length];
var pollNo = 0L;
string? lastWriteInfo = null; // 上次下发结果，显示在表格上方

try
{
    if (once)
    {
        AnsiConsole.Write(await PollAndBuildAsync());
    }
    else if (useLive)
    {
        // 外层循环：Live 期间监听按键；请求写入时退出 Live 做交互，之后重进 Live。
        while (!cts.IsCancellationRequested)
        {
            var writeIndex = -1; // >=0：写该行；-2：打开选择列表
            AnsiConsole.Clear();
            await AnsiConsole.Live(new Table())
                .AutoClear(false)
                .Overflow(VerticalOverflow.Ellipsis)
                .StartAsync(async ctx =>
                {
                    while (!cts.IsCancellationRequested)
                    {
                        ctx.UpdateTarget(await PollAndBuildAsync());
                        ctx.Refresh();
                        // 可被按键打断的等待：轮询间隔内持续检测键盘
                        var elapsed = 0;
                        while (elapsed < intervalMs && !cts.IsCancellationRequested)
                        {
                            if (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(true);
                                if (key.Key is ConsoleKey.Q or ConsoleKey.Escape) { cts.Cancel(); return; }
                                if (key.Key == ConsoleKey.W) { writeIndex = -2; return; }
                                if (key.KeyChar is >= '1' and <= '9')
                                {
                                    var idx = key.KeyChar - '1';
                                    if (idx < tags.Length) { writeIndex = idx; return; }
                                }
                            }
                            await Task.Delay(40, cts.Token);
                            elapsed += 40;
                        }
                    }
                });

            if (cts.IsCancellationRequested) { break; }
            if (writeIndex == -2) { writeIndex = PromptSelectTagIndex(); }
            if (writeIndex >= 0) { await WriteInteractiveAsync(writeIndex); }
        }
    }
    else
    {
        // 滚动模式（--debug / 重定向）：每轮打印一张新表，无交互；日志可穿插其间。
        while (!cts.IsCancellationRequested)
        {
            AnsiConsole.Write(await PollAndBuildAsync());
            await Task.Delay(intervalMs, cts.Token);
        }
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C：正常退出 / normal exit on cancel
}

AnsiConsole.MarkupLine("[grey]已停止 / stopped。[/]");
return 0;

// ---- 读取一轮并更新状态，返回渲染好的表格 / read one cycle, update state, return the table ----
async Task<Table> PollAndBuildAsync()
{
    pollNo++;
    var results = await client.ReadAsync(addresses, cts.Token);
    var byAddr = new Dictionary<string, S7TagValue>(StringComparer.OrdinalIgnoreCase);
    foreach (var r in results)
    {
        byAddr[r.Symbol] = r;
    }
    var noConn = results.Count == 0;

    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn(new TableColumn("[grey]#[/]").RightAligned());
    table.AddColumn("[bold]Tag[/]");
    table.AddColumn("Type");
    table.AddColumn("Acc");
    table.AddColumn("Quality");
    table.AddColumn("[bold]Value[/]");
    table.AddColumn(new TableColumn("Δ").RightAligned());
    table.AddColumn("Address");
    table.Title = new($"[bold]S7Plus[/]  {Markup.Escape(host)}");
    // 标题/脚注文本会被当作 markup 解析：字面量方括号与用户输入的值都必须转义，且保持单行。
    table.Caption = new(Markup.Escape($"poll #{pollNo} · {DateTime.Now:HH:mm:ss} · [1-8] 写入 · [w] 选择 · [q] 退出")
                        + (lastWriteInfo is null ? "" : "  ·  " + Markup.Escape(lastWriteInfo)));

    for (var i = 0; i < tags.Length; i++)
    {
        var (name, address, type, access) = tags[i];
        string quality, valueCell;
        if (byAddr.TryGetValue(address, out var r) && r.IsGood)
        {
            var val = r.Value ?? "";
            var changed = prev[i] is not null && prev[i] != val;
            if (changed) { changeCount[i]++; }
            prev[i] = val;
            quality = "[green]GOOD[/]";
            // 变化的一轮用亮黄加粗高亮，否则常态白色
            valueCell = changed
                ? $"[bold yellow]{Markup.Escape(val)}[/]"
                : $"[white]{Markup.Escape(val)}[/]";
        }
        else if (noConn)
        {
            quality = "[red]NO-CONN[/]";
            valueCell = "[grey]-[/]";
        }
        else if (r.Symbol is not null) // 命中结果但质量不良 / present but bad quality
        {
            quality = "[red]BAD[/]";
            valueCell = "[red]<bad>[/]";
        }
        else // 不在结果中：符号未在 PLC 中解析成功 / not resolved in the PLC
        {
            quality = "[yellow]UNRESOLVED[/]";
            valueCell = "[grey]-[/]";
        }
        var accMarkup = access == "RO" ? "[grey]RO[/]" : "[green]R/W[/]";
        table.AddRow(
            $"[grey]{i + 1}[/]",
            $"[bold]{Markup.Escape(name)}[/]",
            Markup.Escape(type),
            accMarkup,
            quality,
            valueCell,
            changeCount[i].ToString(),
            $"[grey]{Markup.Escape(address)}[/]");
    }
    return table;
}

// ---- 弹出选择列表，返回选中行下标（取消返回 -1）/ selection prompt, returns index or -1 ----
int PromptSelectTagIndex()
{
    const string cancel = "↩ 取消 / cancel";
    var labels = new List<string>();
    for (var i = 0; i < tags.Length; i++)
    {
        var (name, _, type, access) = tags[i];
        labels.Add($"{i + 1}. {name}  [grey]({type}, {access})[/]  = {Markup.Escape(prev[i] ?? "-")}");
    }
    labels.Add(cancel);
    var sel = AnsiConsole.Prompt(new SelectionPrompt<string>()
        .Title("选择要写入的点位 / [green]select a tag to write[/]")
        .PageSize(Math.Min(12, labels.Count + 1))
        .AddChoices(labels));
    return sel == cancel ? -1 : labels.IndexOf(sel);
}

// ---- 交互式下发：显示当前值 → 输入新值 → Enter 触发写入 → 显示结果 ----
async Task WriteInteractiveAsync(int index)
{
    var (name, address, type, access) = tags[index];
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"下发 / write → [bold]{Markup.Escape(name)}[/]  [grey]({Markup.Escape(type)}, {access})[/]");
    AnsiConsole.MarkupLine($"当前值 / current: [cyan]{Markup.Escape(prev[index] ?? "-")}[/]    {Markup.Escape(FormatHint(type))}");
    if (access == "RO")
    {
        AnsiConsole.MarkupLine("[yellow]提示：该点在点表中标记为只读（RO），PLC 可能拒绝写入。[/]");
    }

    string input;
    try
    {
        // TextPrompt：输入后按 Enter 即返回并触发下发；允许空串（交由驱动/PLC 判定）
        input = AnsiConsole.Prompt(new TextPrompt<string>("输入新值 / [green]new value[/]:").AllowEmpty());
    }
    catch (OperationCanceledException)
    {
        return;
    }

    try
    {
        var ok = await client.WriteAsync(address, input, cts.Token);
        lastWriteInfo = ok
            ? $"✓ 写入成功 {name} = {input}  @ {DateTime.Now:HH:mm:ss}"
            : $"✗ 写入失败 {name} = {input}（类型不符/只读/超范围？加 --debug 看日志）";
        if (ok)
        {
            AnsiConsole.MarkupLine($"[green]✓ 写入成功 / OK[/]  {Markup.Escape(name)} = [bold]{Markup.Escape(input)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]✗ 写入失败 / FAILED[/]  {Markup.Escape(name)} = {Markup.Escape(input)}  [grey](类型不符/只读/超范围？加 --debug 看日志)[/]");
        }
    }
    catch (OperationCanceledException)
    {
        return;
    }
    AnsiConsole.MarkupLine("[grey]按任意键返回实时表格 / press any key to return ...[/]");
    Console.ReadKey(true);
}

// 各类型的输入格式提示 / short input-format hint per type
static string FormatHint(string type) => type switch
{
    "Bool" => "填 1 或 0",
    "Real" => "小数点用 .，如 3.14",
    "Word" or "Int" or "DWord" or "DInt" or "UInt" or "UDInt" => "十进制整数，如 1234",
    _ => "按该类型格式填写"
};
