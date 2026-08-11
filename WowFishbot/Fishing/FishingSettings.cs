using System.Text.Json;

namespace WowFishbot.Fishing;

internal sealed record DelayRange(int Min, int Max);

internal sealed class FishingSettings
{
    public string ProcessName { get; init; } = "Wow";
    public int StartVirtualKey { get; init; } = 0xC0;
    public int ExitVirtualKey { get; init; } = 0x77;
    public DelayRange RetryDelayMs { get; init; } = new(150, 200);
    public DelayRange RecastDelayMs { get; init; } = new(150, 200);
    public DelayRange ReactionDelayMs { get; init; } = new(200, 350);
    public DelayRange CursorToClickDelayMs { get; init; } = new(200, 320);
    public DelayRange CursorMoveDurationMs { get; init; } = new(180, 420);
    public int BobberResolveTimeoutMs { get; init; } = 1000;
    public int RenderResolveTimeoutMs { get; init; } = 3000;
    public int BiteTimeoutMs { get; init; } = 22000;
    public int KeyHoldMs { get; init; } = 60;
    public int MouseButtonHoldMs { get; init; } = 55;
    public bool EnableLureReapplication { get; init; } = true;
    public int LureModifierVirtualKey { get; init; } = 0x10;
    public int LureReapplyBeforeExpiryMs { get; init; } = 5000;
    public int LureDurationStalenessMarginMs { get; init; } = 21000;
    public int LureCastStartTimeoutMs { get; init; } = 200;
    public int LureApplyTimeoutMs { get; init; } = 8000;
    public DelayRange LurePreApplyDelayMs { get; init; } = new(150, 200);
    public DelayRange LurePostApplyDelayMs { get; init; } = new(150, 200);
    public bool EnableStateSounds { get; init; } = true;
    public bool EnableBackgroundInput { get; init; }
    public bool EnableFileLogging { get; init; }
    public int LogMaxBytes { get; init; } = 2 * 1024 * 1024;
    public int LogArchiveCount { get; init; } = 3;
    public double AspectRatioTolerance { get; init; } = 0.02;
    public double FieldOfView { get; init; } = 95.0;

    public static FishingSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fishing-controller.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<FishingSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException($"Configuration '{path}' was empty.")
            : new FishingSettings();
        settings.Validate(path);
        return settings;
    }

    private void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(ProcessName)) throw new InvalidOperationException($"ProcessName is missing in '{path}'.");
        if (StartVirtualKey is < 1 or > 254 || ExitVirtualKey is < 1 or > 254 || StartVirtualKey == ExitVirtualKey)
            throw new InvalidOperationException($"Virtual keys in '{path}' must be distinct values from 1 through 254.");
        ValidateRange(RetryDelayMs, nameof(RetryDelayMs), path);
        ValidateRange(RecastDelayMs, nameof(RecastDelayMs), path);
        ValidateRange(ReactionDelayMs, nameof(ReactionDelayMs), path);
        ValidateRange(CursorToClickDelayMs, nameof(CursorToClickDelayMs), path);
        ValidateRange(CursorMoveDurationMs, nameof(CursorMoveDurationMs), path);
        ValidateRange(LurePreApplyDelayMs, nameof(LurePreApplyDelayMs), path);
        ValidateRange(LurePostApplyDelayMs, nameof(LurePostApplyDelayMs), path);
        if (BobberResolveTimeoutMs is < 100 or > 30000 || RenderResolveTimeoutMs is < 100 or > 30000 || BiteTimeoutMs is < 1000 or > 60000)
            throw new InvalidOperationException($"Invalid detection timeout in '{path}'.");
        if (KeyHoldMs is < 20 or > 500 || MouseButtonHoldMs is < 20 or > 500)
            throw new InvalidOperationException($"Input hold durations in '{path}' must be 20 through 500 ms.");
        if (LureModifierVirtualKey is < 1 or > 254 || LureModifierVirtualKey == StartVirtualKey || LureModifierVirtualKey == ExitVirtualKey ||
            LureReapplyBeforeExpiryMs is < 0 or > 60000 || LureDurationStalenessMarginMs is < 0 or > 60000 ||
            LureCastStartTimeoutMs is < 100 or > 2000 || LureApplyTimeoutMs is < 1000 or > 30000)
            throw new InvalidOperationException($"Invalid lure setting in '{path}'.");
        if (LogMaxBytes is < 65536 or > 100 * 1024 * 1024 || LogArchiveCount is < 0 or > 20)
            throw new InvalidOperationException($"Invalid log limit in '{path}'.");
        if (!double.IsFinite(AspectRatioTolerance) || AspectRatioTolerance is < 0 or > 0.25)
            throw new InvalidOperationException($"Invalid aspect-ratio tolerance in '{path}'.");
        if (!double.IsFinite(FieldOfView) || FieldOfView is < 25 or > 150)
            throw new InvalidOperationException($"FieldOfView in '{path}' must be between 25 and 150.");
    }

    private static void ValidateRange(DelayRange? range, string name, string path)
    {
        if (range is null || range.Min < 0 || range.Max < range.Min || range.Max > 60000)
            throw new InvalidOperationException($"Invalid delay range '{name}' in '{path}'.");
    }
}
