using System.Diagnostics;
using WowFishbot.Interop;
using WowFishbot.Memory;

namespace WowFishbot.Fishing;

internal sealed class FishingController
{
    private const double CalibratedCameraFovRadians = 1.75;
    private const double CameraFovOffsetRadians = 0.8;
    private const double CameraFovToleranceRadians = 0.03;
    private const string MouseButtonHeldFailure = FishingInputController.MouseButtonHeldFailure;
    private static readonly (int Key, string Name)[] MovementKeys =
    [
        (0x57, "W"), (0x41, "A"), (0x53, "S"), (0x44, "D"),
        (0x51, "Q"), (0x45, "E"), (0x25, "Left"), (0x26, "Up"),
        (0x27, "Right"), (0x28, "Down"), (0x20, "Space")
    ];

    private readonly ProcessMemoryReader _memory;
    private readonly FishingSettings _settings;
    private readonly FishingInputController _input;
    private readonly int? _parentProcessId;
    private bool _exitRequested;

    internal FishingController(ProcessMemoryReader memory, FishingSettings settings, int? parentProcessId)
    {
        _memory = memory;
        _settings = settings;
        _parentProcessId = parentProcessId;
        _input = new FishingInputController(memory, settings, StopReason);
    }

    internal static Mutex AcquireSingleInstance()
    {
        var mutex = new Mutex(true, "Local\\WowFishbot.Controller", out var createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        throw new InvalidOperationException("Another fishbot instance is already running.");
    }

    internal int Run()
    {
        using var process = Process.GetProcessById(_memory.Info.ProcessId);
        var window = process.MainWindowHandle;
        if (window == IntPtr.Zero) throw new InvalidOperationException("Could not resolve the client window.");
        var viewport = ReadValidatedViewport(window);

        Console.WriteLine($"CONTROLLER_READY: viewport={viewport.Width}x{viewport.Height}; start=0x{_settings.StartVirtualKey:X2}; exit=0x{_settings.ExitVirtualKey:X2}; backgroundInput={_settings.EnableBackgroundInput}.");
        var startKeyWasDown = IsKeyDown(_settings.StartVirtualKey);
        var focusWasValid = false;
        HashSet<ulong> idleSeen = [];

        while (true)
        {
            if (!IsParentProcessAlive()) return 0;
            if (!_memory.IsProcessAlive)
            {
                Console.WriteLine("CONTROLLER_EXIT: client process closed.");
                return 0;
            }
            if (IsExitPressed())
            {
                Console.WriteLine("CONTROLLER_EXIT: exit key pressed.");
                return 0;
            }

            var focused = NativeMethods.IsProcessForeground(_memory.Info.ProcessId);
            var startKeyDown = IsKeyDown(_settings.StartVirtualKey);
            if (!focused)
            {
                if (focusWasValid) Console.WriteLine("IDLE_PAUSED: client is not foreground; inputs disabled.");
                focusWasValid = false;
                startKeyWasDown = startKeyDown;
                Thread.Sleep(50);
                continue;
            }

            if (!focusWasValid)
            {
                idleSeen = ReadCurrentBobberGuids();
                Console.WriteLine("IDLE_READY: press the configured start key for the first cast.");
                focusWasValid = true;
            }

            if (PressedMovementKey() is not null)
            {
                startKeyWasDown = startKeyDown;
                Thread.Sleep(25);
                continue;
            }

            if (startKeyDown && !startKeyWasDown)
            {
                Console.WriteLine("SESSION_START: player-initiated cast detected.");
                SignalState(on: true);
                viewport = ReadValidatedViewport(window);
                RunSession(window, viewport, idleSeen);
                SignalState(on: false);
                if (_exitRequested) return 0;

                Console.WriteLine("IDLE_READY: release movement keys; press the start key for a new session.");
                while (IsKeyDown(_settings.StartVirtualKey) || PressedMovementKey() is not null)
                {
                    if (!_memory.IsProcessAlive || IsExitPressed()) return 0;
                    Thread.Sleep(20);
                }
                startKeyWasDown = false;
                idleSeen = ReadCurrentBobberGuids();
                continue;
            }

            startKeyWasDown = startKeyDown;
            Thread.Sleep(10);
        }
    }

    private void RunSession(IntPtr window, ClientViewport viewport, HashSet<ulong> seen)
    {
        if (!_memory.TryGetLocalPlayer(out var player))
        {
            Console.WriteLine("SESSION_STOP: local player was not resolved.");
            return;
        }

        var expectedAnimationMethod = _memory.Info.ModuleBase + ClientOffsets.BobberAnimationMethodRva;
        var mouseoverAddress = _memory.Info.ModuleBase + ClientOffsets.MouseoverGuidRva;
        var cameraManagerAddress = _memory.Info.ModuleBase + ClientOffsets.CameraManagerRva;
        var catches = 0;
        var lureDisabledForSession = false;
        var castNumber = 1;
        var castClock = Stopwatch.StartNew();

        while (true)
        {
            if (StopReason() is { } initialStop) { Stop(initialStop, catches); return; }

            var bobber = ResolveNewBobber(player.Guid, seen, _settings.BobberResolveTimeoutMs, out var resolveFailure);
            if (resolveFailure is not null) { Stop(resolveFailure, catches); return; }
            if (bobber is null)
            {
                var retryDelay = NextDelay(_settings.RetryDelayMs);
                Console.WriteLine($"CAST {castNumber}: no bobber within {_settings.BobberResolveTimeoutMs}ms; retry in {retryDelay}ms.");
                bobber = ResolveNewBobber(player.Guid, seen, retryDelay, out resolveFailure, pollMs: 20);
                if (resolveFailure is not null) { Stop(resolveFailure, catches); return; }
                if (bobber is null)
                {
                    castNumber++;
                    Console.WriteLine($"CAST {castNumber}: retry cast.");
                    _input.SendKey(window, (byte)_settings.StartVirtualKey, _settings.KeyHoldMs);
                    castClock.Restart();
                    continue;
                }
                Console.WriteLine($"CAST {castNumber}: late bobber detected; retry suppressed.");
            }

            seen.Add(bobber.ObjectGuid);
            Console.WriteLine($"CAST {castNumber}: bobber=0x{bobber.ObjectGuid:X16} castAge={castClock.Elapsed.TotalSeconds:F3}s.");
            var render = ResolveRenderableBobber(player.Guid, bobber.ObjectGuid, expectedAnimationMethod);
            if (render is null)
            {
                Stop($"CAST {castNumber} render model was not readable", catches);
                return;
            }

            var manualCatch = false;

            var biteClock = Stopwatch.StartNew();
            var bite = WaitForBite(render.ObjectAddress, render.InitialAnimation, out var biteFailure);
            if (biteFailure is not null) { Stop(biteFailure, catches); return; }

            var safeForLure = false;
            string waitFailure;
            if (bite)
            {
                var reactionDelay = NextDelay(_settings.ReactionDelayMs);
                Console.WriteLine($"CAST {castNumber}: BITE reaction={reactionDelay}ms castAge={castClock.Elapsed.TotalSeconds:F3}s.");
                if (!WaitInterruptibly(reactionDelay, out waitFailure)) { Stop(waitFailure, catches); return; }
                manualCatch |= _input.GameMouseButtonHeld;
                var caught = false;
                var pixelX = 0;
                var pixelY = 0;
                var catchFailure = string.Empty;
                if (!manualCatch)
                    caught = TryCatchBobber(window, viewport, render, cameraManagerAddress, mouseoverAddress,
                        out pixelX, out pixelY, out catchFailure);
                manualCatch |= catchFailure == MouseButtonHeldFailure;
                if (caught)
                {
                    catches++;
                    safeForLure = true;
                    Console.WriteLine($"CATCH {catches}: bite={biteClock.Elapsed.TotalSeconds:F3}s pixel=({pixelX},{pixelY}).");
                }
                else if (manualCatch)
                {
                    Console.WriteLine($"MANUAL_CATCH: {MouseButtonHeldFailure}; waiting for bobber removal.");
                    if (!WaitForBobberRemoval(player.Guid, render.ObjectGuid, out waitFailure))
                    {
                        Stop(waitFailure, catches);
                        return;
                    }
                    safeForLure = true;
                }
                else Console.WriteLine($"CLICK_REJECTED: {catchFailure}.");
            }
            else
            {
                Console.WriteLine($"CAST {castNumber}: no bite observed; no click.");
                safeForLure = true;
            }

            if (safeForLure && _settings.EnableLureReapplication && !lureDisabledForSession)
            {
                switch (PrepareLure(window, player.ObjectAddress, out var lureFailure))
                {
                    case LurePreparationResult.Interrupted:
                        Stop(lureFailure, catches);
                        return;
                    case LurePreparationResult.DisableForSession:
                        lureDisabledForSession = true;
                        Console.WriteLine($"LURE_DISABLED: {lureFailure}; continuing without further lure attempts until the next session.");
                        break;
                }
            }

            var recastDelay = NextDelay(_settings.RecastDelayMs);
            if (!WaitInterruptibly(recastDelay, out waitFailure)) { Stop(waitFailure, catches); return; }
            foreach (var current in _memory.FindOwnedFishingBobbers(player.Guid)) seen.Add(current.ObjectGuid);
            if (StopReason() is { } castStop) { Stop(castStop, catches); return; }
            castNumber++;
            Console.WriteLine($"CAST {castNumber}: recast after {recastDelay}ms.");
            _input.SendKey(window, (byte)_settings.StartVirtualKey, _settings.KeyHoldMs);
            castClock.Restart();
        }
    }

    private OwnedFishingBobber? ResolveNewBobber(ulong playerGuid, HashSet<ulong> seen, int timeoutMs,
        out string? failure, int pollMs = 60)
    {
        failure = null;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (StopReason() is { } stop) { failure = stop; return null; }
            var result = _memory.FindOwnedFishingBobbers(playerGuid)
                .Where(candidate => !seen.Contains(candidate.ObjectGuid))
                .MaxBy(candidate => candidate.ObjectGuid);
            if (result is not null) return result;
            Thread.Sleep(pollMs);
        }
        return null;
    }

    private RenderableBobber? ResolveRenderableBobber(ulong playerGuid, ulong bobberGuid, uint expectedMethod)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < _settings.RenderResolveTimeoutMs)
        {
            if (StopReason() is not null) return null;
            var current = _memory.FindOwnedFishingBobbers(playerGuid).FirstOrDefault(candidate => candidate.ObjectGuid == bobberGuid);
            if (current is not null)
            {
                _memory.TryReadUInt32(current.ObjectAddress + ClientOffsets.BobberRenderModel, out var model);
                _memory.TryReadUInt32(model, out var vtable);
                _memory.TryReadUInt32(vtable + 0x4C, out var method);
                _memory.TryReadUInt32(model + 0x10, out var animation);
                if (method == expectedMethod) return new RenderableBobber(current.ObjectAddress, current.ObjectGuid, animation);
            }
            Thread.Sleep(30);
        }
        return null;
    }

    private bool WaitForBite(uint objectAddress, uint initialAnimation, out string? failure)
    {
        failure = null;
        var previous = initialAnimation == 8 ? 1u : initialAnimation;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < _settings.BiteTimeoutMs)
        {
            if (StopReason() is { } stop) { failure = stop; return false; }
            _memory.TryReadUInt32(objectAddress + ClientOffsets.BobberRenderModel, out var model);
            _memory.TryReadUInt32(model + 0x10, out var animation);
            if (animation == 8 && previous != 8) return true;
            previous = animation;
            Thread.Sleep(15);
        }
        return false;
    }

    private bool TryCatchBobber(IntPtr window, ClientViewport viewport, RenderableBobber bobber,
        uint cameraManagerAddress, uint mouseoverAddress, out int pixelX, out int pixelY, out string failure)
    {
        if (_input.IsBackgroundActive)
        {
            if (!TryProjectBobber(viewport, bobber.ObjectAddress, cameraManagerAddress,
                    out pixelX, out pixelY, out failure)) return false;
            return _input.TryBackgroundCatch(window, viewport, mouseoverAddress, bobber.ObjectGuid,
                pixelX, pixelY, out failure);
        }
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            pixelX = pixelY = 0;
            failure = "cursor position unavailable";
            return false;
        }
        pixelX = cursor.X - viewport.Origin.X;
        pixelY = cursor.Y - viewport.Origin.Y;
        _memory.TryReadUInt64(mouseoverAddress, out var mouseoverGuid);
        if (mouseoverGuid != bobber.ObjectGuid)
        {
            if (!TryMoveCursorToBobber(window, viewport, bobber.ObjectAddress, cameraManagerAddress,
                    out pixelX, out pixelY, out failure))
                return false;
            var settleDelay = NextDelay(_settings.CursorToClickDelayMs);
            if (!WaitInterruptibly(settleDelay, out failure)) return false;
        }
        return _input.TryFocusedCatch(window, viewport, mouseoverAddress, bobber.ObjectGuid,
            pixelX, pixelY, out failure);
    }

    private LurePreparationResult PrepareLure(IntPtr window, uint playerAddress, out string failure)
    {
        failure = string.Empty;
        var lure = TryReadLure(playerAddress);
        if (lure is null)
        {
            Console.WriteLine("LURE_STATE: unavailable; continuing without reapplication.");
            return LurePreparationResult.Continue;
        }

        var requiredMs = checked(_settings.BiteTimeoutMs + _settings.LureReapplyBeforeExpiryMs + _settings.LureDurationStalenessMarginMs);
        Console.WriteLine($"LURE_STATE: item={lure.ItemEntry} enchant={lure.EnchantId} remaining={lure.DurationMs}ms required={requiredMs}ms.");
        if (lure.EnchantId != 0 && lure.DurationMs > requiredMs) return LurePreparationResult.Continue;

        var preDelay = NextDelay(_settings.LurePreApplyDelayMs);
        if (!WaitInterruptibly(preDelay, out failure)) return LurePreparationResult.Interrupted;
        _input.SendModifiedKey(window, (byte)_settings.LureModifierVirtualKey,
            (byte)_settings.StartVirtualKey, _settings.KeyHoldMs);

        var castStartClock = Stopwatch.StartNew();
        uint castingSpellId = 0;
        var castingStateReadable = false;
        while (castStartClock.ElapsedMilliseconds < _settings.LureCastStartTimeoutMs)
        {
            if (StopReason() is { } stop) { failure = stop; return LurePreparationResult.Interrupted; }
            if (_memory.TryReadUInt32(playerAddress + ClientOffsets.UnitCastingSpellId, out castingSpellId))
            {
                castingStateReadable = true;
                if (castingSpellId != 0) break;
            }
            Thread.Sleep(10);
        }
        if (castingStateReadable && castingSpellId == 0)
        {
            failure = $"lure cast did not start within {_settings.LureCastStartTimeoutMs}ms";
            return LurePreparationResult.DisableForSession;
        }
        if (castingSpellId != 0)
            Console.WriteLine($"LURE_CAST_STARTED: spell={castingSpellId} detection={castStartClock.ElapsedMilliseconds}ms.");
        else
            Console.WriteLine("LURE_CAST_STATE: unreadable; retaining enchant-confirmation fallback.");

        var clock = Stopwatch.StartNew();
        FishingLureState? confirmed = null;
        while (clock.ElapsedMilliseconds < _settings.LureApplyTimeoutMs)
        {
            if (StopReason() is { } stop) { failure = stop; return LurePreparationResult.Interrupted; }
            var current = TryReadLure(playerAddress);
            if (current is not null && current.EnchantId != 0 &&
                (current.EnchantId != lure.EnchantId || current.DurationMs > lure.DurationMs + 30000))
            {
                confirmed = current;
                break;
            }
            Thread.Sleep(50);
        }
        if (confirmed is null)
        {
            failure = $"lure application was not confirmed within {_settings.LureApplyTimeoutMs}ms";
            return LurePreparationResult.DisableForSession;
        }

        Console.WriteLine($"LURE_APPLIED: enchant={confirmed.EnchantId} duration={confirmed.DurationMs}ms confirmation={clock.ElapsedMilliseconds}ms.");
        return WaitInterruptibly(NextDelay(_settings.LurePostApplyDelayMs), out failure)
            ? LurePreparationResult.Continue
            : LurePreparationResult.Interrupted;
    }

    private FishingLureState? TryReadLure(uint playerAddress)
    {
        const uint playerFieldInvSlotHead = 0x144;
        const int mainHandSlot = 15;
        const uint temporaryEnchantIdIndex = 0x19;
        if (!_memory.TryReadUInt32(playerAddress + ClientOffsets.Descriptors, out var playerDescriptors) ||
            !ProcessMemoryReader.IsValidPointer(playerDescriptors)) return null;
        var mainHandGuidIndex = playerFieldInvSlotHead + (uint)(mainHandSlot * 2);
        if (!_memory.TryReadUInt64(playerDescriptors + mainHandGuidIndex * 4, out var itemGuid) || itemGuid == 0 ||
            !_memory.TryFindObjectByGuid(itemGuid, 1, out var item) ||
            !_memory.TryReadUInt32(item.ObjectAddress + ClientOffsets.Descriptors, out var itemDescriptors) ||
            !ProcessMemoryReader.IsValidPointer(itemDescriptors)) return null;
        if (!_memory.TryReadUInt32(itemDescriptors + temporaryEnchantIdIndex * 4, out var enchantId) ||
            !_memory.TryReadUInt32(itemDescriptors + (temporaryEnchantIdIndex + 1) * 4, out var durationMs)) return null;
        return new FishingLureState(item.Entry, enchantId, durationMs);
    }

    private bool TryMoveCursorToBobber(IntPtr window, ClientViewport viewport, uint objectAddress,
        uint cameraManagerAddress, out int clientX, out int clientY, out string failure)
    {
        clientX = clientY = 0;
        failure = string.Empty;
        if (_input.GameMouseButtonHeld) { failure = MouseButtonHeldFailure; return false; }
        if (!TryProjectBobber(viewport, objectAddress, cameraManagerAddress, out clientX, out clientY, out failure))
            return false;
        return MoveCursorInterpolated(window, viewport, clientX, clientY, out _, out failure);
    }

    private bool TryProjectBobber(ClientViewport viewport, uint objectAddress, uint cameraManagerAddress,
        out int clientX, out int clientY, out string failure)
    {
        clientX = clientY = 0;
        failure = string.Empty;
        if (!_memory.TryReadSingle(objectAddress + ClientOffsets.BobberWorldY, out var worldY) ||
            !_memory.TryReadSingle(objectAddress + ClientOffsets.BobberWorldX, out var worldX) ||
            !_memory.TryReadSingle(objectAddress + ClientOffsets.BobberWorldZ, out var worldZ))
        {
            failure = "bobber coordinates unreadable";
            return false;
        }
        if (!_memory.TryReadUInt32(cameraManagerAddress, out var cameraManager) ||
            !_memory.TryReadUInt32(cameraManager + 0x7E20, out var camera) ||
            !_memory.TryReadBytes(camera, 0x44, out var bytes))
        {
            failure = "camera unreadable";
            return false;
        }

        var cameraY = BitConverter.ToSingle(bytes, 0x08);
        var cameraX = BitConverter.ToSingle(bytes, 0x0C);
        var cameraZ = BitConverter.ToSingle(bytes, 0x10);
        var cameraFov = BitConverter.ToSingle(bytes, 0x40);
        var expectedCameraFov = CameraFovOffsetRadians + _settings.FieldOfView / 100.0;
        if (!float.IsFinite(cameraFov) || Math.Abs(cameraFov - expectedCameraFov) > CameraFovToleranceRadians)
        {
            failure = $"configured FOV {_settings.FieldOfView:F1} predicts camera FOV {expectedCameraFov:F3}, but memory reports {cameraFov:F3}";
            return false;
        }
        var matrix = new float[9];
        for (var i = 0; i < matrix.Length; i++) matrix[i] = BitConverter.ToSingle(bytes, 0x14 + i * 4);
        var projection = ProjectBobber(worldX, worldY, worldZ, cameraX, cameraY, cameraZ, matrix,
            cameraFov, viewport.Width, viewport.Height);
        clientX = (int)Math.Round(projection.X);
        clientY = (int)Math.Round(projection.Y);
        if (projection.Depth <= 0 || clientX < 0 || clientX >= viewport.Width || clientY < 0 || clientY >= viewport.Height)
        {
            failure = $"projection pixel=({clientX},{clientY}) depth={projection.Depth:F3}";
            return false;
        }
        return true;
    }

    private bool MoveCursorInterpolated(IntPtr window, ClientViewport viewport, int targetClientX, int targetClientY,
        out int durationMs, out string failure)
    {
        durationMs = 0;
        failure = string.Empty;
        if (!NativeMethods.GetCursorPos(out var start)) { failure = "cursor position unavailable"; return false; }
        var targetX = viewport.Origin.X + targetClientX;
        var targetY = viewport.Origin.Y + targetClientY;
        var startX = start.X;
        var startY = start.Y;
        var deltaX = targetX - startX;
        var deltaY = targetY - startY;
        var distance = Math.Sqrt(deltaX * (double)deltaX + deltaY * (double)deltaY);
        durationMs = Math.Clamp((int)Math.Round(155 + distance * 0.32) + Random.Shared.Next(-20, 21),
            _settings.CursorMoveDurationMs.Min, _settings.CursorMoveDurationMs.Max);
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < durationMs)
        {
            if (StopReason() is { } stop) { failure = stop; return false; }
            if (_input.GameMouseButtonHeld) { failure = MouseButtonHeldFailure; return false; }
            if (!viewport.IsCurrent(window)) { failure = "client viewport changed"; return false; }
            var t = Math.Clamp(clock.Elapsed.TotalMilliseconds / durationMs, 0, 1);
            var eased = t * t * (3 - 2 * t);
            var nextX = startX + (int)Math.Round(deltaX * eased);
            var nextY = startY + (int)Math.Round(deltaY * eased);
            if (!NativeMethods.SetCursorPos(nextX, nextY))
            {
                failure = "SetCursorPos failed";
                return false;
            }
            Thread.Sleep(8);
        }
        if (_input.GameMouseButtonHeld) { failure = MouseButtonHeldFailure; return false; }
        if (NativeMethods.SetCursorPos(targetX, targetY)) return true;
        failure = "final SetCursorPos failed";
        return false;
    }

    private bool WaitForBobberRemoval(ulong playerGuid, ulong bobberGuid, out string failure)
    {
        failure = string.Empty;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 10000)
        {
            if (StopReason() is { } stop) { failure = stop; return false; }
            if (_memory.FindOwnedFishingBobbers(playerGuid).All(item => item.ObjectGuid != bobberGuid))
            {
                Console.WriteLine($"MANUAL_CATCH_DONE: bobber removed after {clock.ElapsedMilliseconds}ms.");
                return true;
            }
            Thread.Sleep(25);
        }
        Console.WriteLine("MANUAL_CATCH_TIMEOUT: bobber remained for 10000ms; resuming.");
        return true;
    }

    private static ScreenProjection ProjectBobber(float worldX, float worldY, float worldZ,
        float cameraX, float cameraY, float cameraZ, float[] matrix, float cameraFov, int width, int height)
    {
        var dx = worldX - cameraX;
        var dy = worldY - cameraY;
        var dz = worldZ - 0.95f - cameraZ;
        var depth = dy * matrix[0] + dx * matrix[1] + dz * matrix[2];
        var right = dy * matrix[3] + dx * matrix[4] + dz * matrix[5];
        var up = dy * matrix[6] + dx * matrix[7] + dz * matrix[8];
        var viewportScale = height / 1080.0;
        var centerX = width / 2.0 + (963.935465487425 - 960.0) * viewportScale;
        var centerY = height * (501.6182034479883 / 1080.0);
        var fovScale = Math.Tan(CalibratedCameraFovRadians / 2.0) / Math.Tan(cameraFov / 2.0);
        var horizontalScale = 964.8055111103448 * viewportScale * fovScale;
        var verticalScale = height * (922.4937830093356 / 1080.0) * fovScale;
        return new ScreenProjection(centerX - horizontalScale * right / depth,
            centerY - verticalScale * up / depth, depth);
    }

    private HashSet<ulong> ReadCurrentBobberGuids()
    {
        if (!_memory.TryGetLocalPlayer(out var player)) return [];
        return _memory.FindOwnedFishingBobbers(player.Guid).Select(item => item.ObjectGuid).ToHashSet();
    }

    private bool WaitInterruptibly(int delayMs, out string failure)
    {
        failure = string.Empty;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < delayMs)
        {
            if (StopReason() is { } stop) { failure = stop; return false; }
            Thread.Sleep(10);
        }
        return true;
    }

    private string? StopReason()
    {
        if (!IsParentProcessAlive()) return "launcher closed";
        if (!_memory.IsProcessAlive) return "client process closed";
        if (IsExitPressed()) { _exitRequested = true; return "exit key pressed"; }
        var foreground = NativeMethods.IsProcessForeground(_memory.Info.ProcessId);
        if (!foreground && !_settings.EnableBackgroundInput) return "client lost foreground focus";
        var movement = foreground ? PressedMovementKey() : null;
        return movement is null ? null : $"movement key {movement} pressed";
    }

    private bool IsParentProcessAlive()
    {
        if (_parentProcessId is null) return true;
        try
        {
            using var parent = Process.GetProcessById(_parentProcessId.Value);
            return !parent.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void Stop(string reason, int catches) => Console.WriteLine($"SESSION_STOP: {reason}; caught={catches}.");
    private int NextDelay(DelayRange range) => Random.Shared.Next(range.Min, checked(range.Max + 1));
    private bool IsExitPressed() => IsKeyDown(_settings.ExitVirtualKey);
    private static bool IsKeyDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;

    private static string? PressedMovementKey()
    {
        foreach (var (key, name) in MovementKeys)
            if (IsKeyDown(key)) return name;
        return null;
    }

    private ClientViewport ReadValidatedViewport(IntPtr window)
    {
        if (!NativeMethods.GetClientRect(window, out var rect)) throw new InvalidOperationException("Could not read the client rectangle.");
        var origin = new Point();
        if (!NativeMethods.ClientToScreen(window, ref origin)) throw new InvalidOperationException("Could not read the client screen origin.");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 640 || height < 360) throw new InvalidOperationException($"Client viewport {width}x{height} is too small.");
        var aspect = width / (double)height;
        var supportedAspect = Math.Abs(aspect - 16.0 / 9.0) <= _settings.AspectRatioTolerance ||
                              Math.Abs(aspect - 43.0 / 18.0) <= _settings.AspectRatioTolerance;
        if (!supportedAspect)
            throw new InvalidOperationException($"Viewport {width}x{height} is not a supported 16:9 or ultrawide resolution.");
        return new ClientViewport(origin, width, height);
    }

    private void SignalState(bool on)
    {
        if (!_settings.EnableStateSounds) return;
        try { NativeMethods.MessageBeep(on ? 0x00000040u : 0x00000010u); }
        catch { }
    }
}

internal sealed record ClientViewport(Point Origin, int Width, int Height)
{
    internal bool IsCurrent(IntPtr window)
    {
        if (!NativeMethods.GetClientRect(window, out var rect)) return false;
        var origin = new Point();
        return NativeMethods.ClientToScreen(window, ref origin) &&
               origin.X == Origin.X && origin.Y == Origin.Y &&
               rect.Right - rect.Left == Width && rect.Bottom - rect.Top == Height;
    }
}
internal sealed record RenderableBobber(uint ObjectAddress, ulong ObjectGuid, uint InitialAnimation);
internal sealed record FishingLureState(uint ItemEntry, uint EnchantId, uint DurationMs);
internal sealed record ScreenProjection(double X, double Y, double Depth);
internal enum LurePreparationResult { Continue, DisableForSession, Interrupted }
