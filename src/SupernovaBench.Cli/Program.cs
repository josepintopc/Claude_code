using System.Globalization;
using System.Text.Json;
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

Console.Error.WriteLine(Loc.T("cli.unknown"));
Console.WriteLine(Loc.T("cli.usage"));
return 1;
