using System.Globalization;
using System.Text.Json;
using SupernovaBench.Cli;
using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Cpu;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

// Supernova Bench — linha de comando (roda os mesmos motores do app, sem interface).
// Exemplos:
//   supernova-cli bench cpu                 (multi-core, preset padrão)
//   supernova-cli bench cpu --mode single --quick
//   supernova-cli bench cpu --mode sustained --minutes 2 --json
//   supernova-cli bench memory --quick
//   supernova-cli bench disk --drive D --size 1g
//   supernova-cli stress cpu --seconds 600
//   supernova-cli info

var argList = args.ToList();
string? Opt(string name)
{
    int i = argList.IndexOf(name);
    return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : null;
}
bool Flag(string name) => argList.Contains(name);

Loc.SetLanguage(Opt("--lang") ?? (CultureInfo.CurrentUICulture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en"));

if (argList.Count == 0 || Flag("--help") || Flag("-h"))
{
    Console.WriteLine(Loc.T("cli.usage"));
    return 0;
}

if (argList[0] == "info")
{
    Console.WriteLine($"{AppInfo.Name} {AppInfo.Version}");
    Console.WriteLine($"{Loc.T("hw.cpu.name")}: {MachineInfo.CpuName()}");
    Console.WriteLine($"{Loc.T("hw.cpu.logical")}: {Environment.ProcessorCount}");
    Console.WriteLine($"{Loc.T("hw.cpu.instructions")}: {string.Join(", ", MachineInfo.InstructionSets())}");
    return 0;
}

if (argList.Count >= 2 && argList[0] == "bench" && argList[1] == "cpu")
{
    var mode = (Opt("--mode") ?? "multi") switch
    {
        "single" => CpuBenchMode.SingleCore,
        "sustained" => CpuBenchMode.Sustained,
        _ => CpuBenchMode.MultiCore,
    };
    var preset = Flag("--quick") ? RenderPreset.Quick : RenderPreset.Standard;
    int? threads = int.TryParse(Opt("--threads"), out var t) && t > 0 ? t : null;
    TimeSpan? sustained = double.TryParse(Opt("--minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0
        ? TimeSpan.FromMinutes(m) : null;
    bool json = Flag("--json");

    var renderer = new CpuRenderBench(preset);
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    if (!json)
    {
        Console.WriteLine($"{AppInfo.Name} {AppInfo.Version} — {Loc.T(TestIds.NameKey(CpuBenchmark.TestId(mode)))}");
        Console.WriteLine($"{MachineInfo.CpuName()} · {threads ?? CpuBenchmark.ThreadsFor(mode)} threads · {preset.Width}x{preset.Height} @ {preset.SamplesPerPixel} spp");
    }

    var task = Task.Run(() => CpuBenchmark.Run(renderer, mode, cts.Token, threads, sustained));
    while (!task.IsCompleted)
    {
        await Task.WhenAny(task, Task.Delay(1000));
        if (!json && !task.IsCompleted)
        {
            double pct = 100.0 * renderer.BucketsDone / renderer.Buckets.Count;
            Console.Write($"\r  {Loc.T("cpu.progress")}: {pct,5:0.0}%  ·  {Loc.T("cpu.passes")}: {renderer.PassesDone}   ");
        }
    }

    var result = await task;
    if (result == null)
    {
        Console.WriteLine();
        Console.WriteLine(Loc.T("cpu.cancelled"));
        return 2;
    }

    if (Flag("--save")) new ResultHistory().Add(result);
    if (Opt("--bmp") is { } bmp) BmpWriter.Save(bmp, renderer.Pixels, renderer.Width, renderer.Height);

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, JsonFile.Options));
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine($"  {Loc.T("cpu.score")}: {result.Score.ToString("N0", Loc.Culture)} pts  ({result.Seconds.ToString("N1", Loc.Culture)} s, {result.Passes}x)");
    }
    return 0;
}

if (argList.Count >= 2 && argList[0] == "gpu" && argList[1] == "info")
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(Loc.T("cli.gpu.notWindows")); return 3; }
    return GpuCommands.Info();
}

if (argList.Count >= 2 && argList[0] == "bench" && argList[1] == "gpu")
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(Loc.T("cli.gpu.notWindows")); return 3; }
    int seconds = int.TryParse(Opt("--seconds"), out var sec) && sec > 0 ? sec : 30;
    return GpuCommands.Bench(Opt("--res"), seconds, Flag("--warp"), Flag("--headless"), Flag("--json"), Flag("--save"));
}

if (argList.Count >= 2 && argList[0] == "bench" && argList[1] == "memory")
{
    using var memCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; memCts.Cancel(); };
    return Phase3Commands.BenchMemory(Flag("--quick"), Flag("--json"), Flag("--save"), memCts.Token);
}

if (argList.Count >= 2 && argList[0] == "bench" && argList[1] == "disk")
{
    using var diskCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; diskCts.Cancel(); };
    int runs = int.TryParse(Opt("--runs"), out var rn) && rn > 0 ? rn : 3;
    double secs = double.TryParse(Opt("--seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds) && ds > 0 ? ds : 5;
    return await Phase3Commands.BenchDisk(Opt("--drive"), Opt("--path"), Opt("--size"), runs, secs, Flag("--json"), Flag("--save"), diskCts.Token);
}

if (argList.Count >= 2 && argList[0] == "stress" && argList[1] == "cpu")
{
    double secs = double.TryParse(Opt("--seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cs) && cs > 0 ? cs : 60;
    int? threads = int.TryParse(Opt("--threads"), out var st) && st > 0 ? st : null;
    return Phase3Commands.StressCpu(Opt("--mode"), threads, secs);
}

if (argList.Count >= 2 && argList[0] == "stress" && argList[1] == "ram")
{
    long? mb = long.TryParse(Opt("--mb"), out var mbv) && mbv > 0 ? mbv : null;
    int? percent = int.TryParse(Opt("--percent"), out var pv) && pv is > 0 and <= 95 ? pv : null;
    double? secs = double.TryParse(Opt("--seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var rs) && rs > 0 ? rs : null;
    return Phase3Commands.StressRam(mb, percent, secs);
}

if (argList.Count >= 2 && argList[0] == "disk" && argList[1] == "health")
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(Loc.T("cli.gpu.notWindows")); return 3; }
    return Phase3Commands.DiskHealthInfo();
}

if (argList.Count >= 2 && argList[0] == "games" && argList[1] == "list")
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(Loc.T("cli.gpu.notWindows")); return 3; }
    return GamesCommands.List(Flag("--json"));
}

if (argList.Count >= 2 && argList[0] == "drivers")
{
    if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(Loc.T("cli.gpu.notWindows")); return 3; }
    return argList[1] switch
    {
        "list" => DriversCommands.List(Flag("--problems")),
        "check" => await DriversCommands.Check(),
        "restore-points" => DriversCommands.RestorePointsList(),
        _ => 1,
    };
}

Console.Error.WriteLine(Loc.T("cli.unknown"));
Console.WriteLine(Loc.T("cli.usage"));
return 1;
