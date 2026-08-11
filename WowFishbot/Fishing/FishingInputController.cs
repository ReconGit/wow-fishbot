using System.Diagnostics;
using WowFishbot.Interop;
using WowFishbot.Memory;

namespace WowFishbot.Fishing;

internal sealed class FishingInputController
{
    internal const string MouseButtonHeldFailure = "mouse button held; manual catch required";

    private readonly ProcessMemoryReader _memory;
    private readonly FishingSettings _settings;
    private readonly Func<string?> _stopReason;

    internal FishingInputController(ProcessMemoryReader memory, FishingSettings settings, Func<string?> stopReason)
    {
        _memory = memory;
        _settings = settings;
        _stopReason = stopReason;
    }

    internal bool IsBackgroundActive =>
        _settings.EnableBackgroundInput && !NativeMethods.IsProcessForeground(_memory.Info.ProcessId);

    internal bool GameMouseButtonHeld =>
        NativeMethods.IsProcessForeground(_memory.Info.ProcessId) && PhysicalMouseButtonHeld();

    internal void SendKey(IntPtr window, byte virtualKey, int holdMs)
    {
        var background = IsBackgroundActive;
        SendKeyState(window, virtualKey, keyDown: true, background);
        try { Thread.Sleep(holdMs); }
        finally { SendKeyState(window, virtualKey, keyDown: false, background); }
    }

    internal void SendModifiedKey(IntPtr window, byte modifier, byte virtualKey, int holdMs)
    {
        var background = IsBackgroundActive;
        if (!background)
        {
            SendForegroundModifiedKey(window, modifier, virtualKey, holdMs);
            return;
        }

        // WoW may poll the global modifier state even when the base key is delivered to its background window.
        NativeMethods.keybd_event(modifier, 0, 0, UIntPtr.Zero);
        try
        {
            SendKeyState(window, modifier, keyDown: true, background: true);
            try
            {
                Thread.Sleep(50);
                SendKey(window, virtualKey, holdMs, background: true);
                Thread.Sleep(50);
            }
            finally
            {
                SendKeyState(window, modifier, keyDown: false, background: true);
            }
        }
        finally
        {
            NativeMethods.keybd_event(modifier, 0, NativeMethods.KeyUp, UIntPtr.Zero);
        }
    }

    private void SendForegroundModifiedKey(IntPtr window, byte modifier, byte virtualKey, int holdMs)
    {
        SendKeyState(window, modifier, keyDown: true, background: false);
        try
        {
            Thread.Sleep(50);
            SendKey(window, virtualKey, holdMs, background: false);
            Thread.Sleep(50);
        }
        finally
        {
            SendKeyState(window, modifier, keyDown: false, background: false);
        }
    }

    private void SendKey(IntPtr window, byte virtualKey, int holdMs, bool background)
    {
        SendKeyState(window, virtualKey, keyDown: true, background);
        try { Thread.Sleep(holdMs); }
        finally { SendKeyState(window, virtualKey, keyDown: false, background); }
    }

    private static void SendKeyState(IntPtr window, byte virtualKey, bool keyDown, bool background)
    {
        if (!background)
        {
            NativeMethods.keybd_event(virtualKey, 0, keyDown ? 0 : NativeMethods.KeyUp, UIntPtr.Zero);
            return;
        }

        var scanCode = NativeMethods.MapVirtualKey(virtualKey, 0);
        var state = 1L | ((long)scanCode << 16);
        if (!keyDown) state |= (1L << 30) | (1L << 31);
        var message = keyDown ? NativeMethods.WindowKeyDown : NativeMethods.WindowKeyUp;
        if (!NativeMethods.PostMessage(window, message, (IntPtr)virtualKey, (IntPtr)state))
            throw new InvalidOperationException($"Background key message 0x{message:X} failed for virtual key 0x{virtualKey:X2}.");
    }

    internal bool TryFocusedCatch(IntPtr window, ClientViewport viewport, uint mouseoverAddress,
        ulong expectedGuid, int clientX, int clientY, out string failure)
    {
        failure = _stopReason() ?? string.Empty;
        if (failure.Length != 0) return false;
        if (GameMouseButtonHeld) { failure = MouseButtonHeldFailure; return false; }
        if (!viewport.IsCurrent(window)) { failure = "client viewport changed"; return false; }
        if (!NativeMethods.GetClipCursor(out var previousClip))
        {
            failure = "focused cursor clipping state unavailable";
            return false;
        }

        var clickX = viewport.Origin.X + clientX;
        var clickY = viewport.Origin.Y + clientY;
        var clickClip = OnePixelRect(clickX, clickY);
        if (!NativeMethods.SetCursorClip(ref clickClip))
        {
            failure = "focused cursor confinement failed";
            return false;
        }

        try
        {
            if (!NativeMethods.SetCursorPos(clickX, clickY))
            {
                failure = "focused cursor positioning failed under confinement";
                return false;
            }
            if (!WaitForMouseover(mouseoverAddress, expectedGuid, 180, out var observedGuid, out failure))
            {
                if (failure.Length == 0)
                    failure = $"focused confined mouseover=0x{observedGuid:X16}; expected=0x{expectedGuid:X16}";
                return false;
            }
            NativeMethods.mouse_event(NativeMethods.RightButtonDown, 0, 0, 0, UIntPtr.Zero);
            try { Thread.Sleep(_settings.MouseButtonHoldMs); }
            finally { NativeMethods.mouse_event(NativeMethods.RightButtonUp, 0, 0, 0, UIntPtr.Zero); }
        }
        finally
        {
            if (!RestoreCursorClip(ref previousClip))
                AppendFailure(ref failure, "focused cursor confinement could not be restored");
        }
        return failure.Length == 0;
    }

    internal bool TryBackgroundCatch(IntPtr window, ClientViewport viewport, uint mouseoverAddress,
        ulong expectedGuid, int clientX, int clientY, out string failure)
    {
        failure = string.Empty;
        if (PhysicalMouseButtonHeld())
        {
            failure = "host mouse button held; atomic background catch skipped";
            return false;
        }
        if (!NativeMethods.GetCursorPos(out var saved))
        {
            failure = "host cursor position unavailable";
            return false;
        }
        if (!NativeMethods.GetClipCursor(out var previousClip))
        {
            failure = "host cursor clipping state unavailable";
            return false;
        }

        var targetX = viewport.Origin.X + clientX;
        var targetY = viewport.Origin.Y + clientY;
        var moved = false;
        var clipped = false;
        var caught = false;
        try
        {
            var targetClip = OnePixelRect(targetX, targetY);
            if (!NativeMethods.SetCursorClip(ref targetClip))
            {
                failure = "atomic background cursor confinement failed";
                return false;
            }
            clipped = true;
            if (!NativeMethods.SetCursorPos(targetX, targetY))
            {
                failure = "background cursor bridge SetCursorPos failed";
                return false;
            }
            moved = true;
            if (!SendWindowMessage(window, NativeMethods.WindowMouseMove, IntPtr.Zero,
                    PackMousePosition(clientX, clientY)))
            {
                failure = "background cursor bridge WM_MOUSEMOVE timed out or failed";
                return false;
            }
            if (!WaitForMouseover(mouseoverAddress, expectedGuid, 30, out var observedGuid, out failure))
            {
                if (failure.Length == 0)
                    failure = $"atomic background mouseover=0x{observedGuid:X16}; expected=0x{expectedGuid:X16}";
                return false;
            }
            if (!SendRightButton(window, clientX, clientY, keyDown: true))
            {
                failure = "atomic background WM_RBUTTONDOWN failed";
                return false;
            }
            Thread.Sleep(_settings.MouseButtonHoldMs);
            if (!SendRightButton(window, clientX, clientY, keyDown: false))
            {
                PostRightButton(window, clientX, clientY, keyDown: false);
                failure = "atomic background WM_RBUTTONUP failed";
                return false;
            }
            caught = true;
        }
        finally
        {
            var savedPositionHeld = false;
            if (clipped && moved)
            {
                var savedClip = OnePixelRect(saved.X, saved.Y);
                savedPositionHeld = NativeMethods.SetCursorClip(ref savedClip) &&
                                    NativeMethods.SetCursorPos(saved.X, saved.Y);
                if (savedPositionHeld) Thread.Sleep(16);
            }
            if (clipped && !RestoreCursorClip(ref previousClip))
                AppendFailure(ref failure, "previous host cursor confinement could not be restored");
            if (moved && !savedPositionHeld && !NativeMethods.SetCursorPos(saved.X, saved.Y))
                AppendFailure(ref failure, "host cursor restoration failed");
        }
        return caught && failure.Length == 0;
    }

    private bool WaitForMouseover(uint address, ulong expectedGuid, int timeoutMs,
        out ulong observedGuid, out string failure)
    {
        observedGuid = 0;
        failure = string.Empty;
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (_stopReason() is { } stop) { failure = stop; return false; }
            _memory.TryReadUInt64(address, out observedGuid);
            if (observedGuid == expectedGuid) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    private static bool SendRightButton(IntPtr window, int clientX, int clientY, bool keyDown) =>
        SendWindowMessage(window,
            keyDown ? NativeMethods.WindowRightButtonDown : NativeMethods.WindowRightButtonUp,
            keyDown ? (IntPtr)NativeMethods.MouseKeyRightButton : IntPtr.Zero,
            PackMousePosition(clientX, clientY));

    private static bool SendWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.SendMessageTimeout(window, message, wParam, lParam,
            NativeMethods.SendMessageAbortIfHung, 50, out _) != IntPtr.Zero;

    private static bool PostRightButton(IntPtr window, int clientX, int clientY, bool keyDown) =>
        NativeMethods.PostMessage(window,
            keyDown ? NativeMethods.WindowRightButtonDown : NativeMethods.WindowRightButtonUp,
            keyDown ? (IntPtr)NativeMethods.MouseKeyRightButton : IntPtr.Zero,
            PackMousePosition(clientX, clientY));

    private static IntPtr PackMousePosition(int clientX, int clientY) =>
        (IntPtr)((clientY << 16) | (clientX & 0xffff));

    private static Rect OnePixelRect(int x, int y) => new()
    {
        Left = x,
        Top = y,
        Right = x + 1,
        Bottom = y + 1
    };

    private static bool RestoreCursorClip(ref Rect previousClip)
    {
        if (NativeMethods.SetCursorClip(ref previousClip)) return true;
        NativeMethods.ReleaseCursorClip(IntPtr.Zero);
        return false;
    }

    private static void AppendFailure(ref string failure, string addition) =>
        failure = failure.Length == 0 ? addition : $"{failure}; {addition}";

    private static bool PhysicalMouseButtonHeld() => IsKeyDown(0x01) || IsKeyDown(0x02);
    private static bool IsKeyDown(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
}
