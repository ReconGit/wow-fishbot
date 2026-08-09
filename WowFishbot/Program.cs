using System.Globalization;
using WowFishbot.Fishing;
using WowFishbot.Infrastructure;
using WowFishbot.Interop;
using WowFishbot.Memory;

namespace WowFishbot;

internal static class Program
{
    public static int Main(string[] args)
    {
        var originalOutput = Console.Out;
        RotatingTextWriter? output = null;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var options = AppOptions.Parse(args);
            var settings = FishingSettings.Load();
            using var instance = FishingController.AcquireSingleInstance();
            if (options.EnableDebugPrivilege) NativeMethods.EnableDebugPrivilege();
            output = !settings.EnableFileLogging || options.OutputPath is null
                ? null
                : new RotatingTextWriter(Path.GetFullPath(options.OutputPath), settings.LogMaxBytes, settings.LogArchiveCount);
            if (output is not null) Console.SetOut(output);

            using var memory = ProcessMemoryReader.Open(options.ProcessId, settings.ProcessName);
            return new FishingController(memory, settings, options.ParentProcessId).Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            if (output is not null) output.WriteLine($"error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.SetOut(originalOutput);
            output?.Dispose();
        }
    }
}

internal sealed record AppOptions(int? ProcessId, int? ParentProcessId, bool EnableDebugPrivilege, string? OutputPath)
{
    public static AppOptions Parse(string[] args)
    {
        int? processId = null;
        int? parentProcessId = null;
        var enableDebugPrivilege = false;
        string? outputPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid" when i + 1 < args.Length:
                    processId = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--debug-privilege":
                    enableDebugPrivilege = true;
                    break;
                case "--parent-pid" when i + 1 < args.Length:
                    parentProcessId = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--output" when i + 1 < args.Length:
                    outputPath = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown or incomplete option: {args[i]}");
            }
        }
        return new AppOptions(processId, parentProcessId, enableDebugPrivilege, outputPath);
    }
}
