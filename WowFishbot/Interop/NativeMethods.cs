using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WowFishbot.Interop;

internal static class NativeMethods
{
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint Th32csSnapModule = 0x00000008;
    internal const uint Th32csSnapModule32 = 0x00000010;
    internal const uint InvalidHandleValue = 0xffffffff;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x00000002;
    internal const uint KeyUp = 0x0002;
    internal const uint RightButtonDown = 0x0008;
    internal const uint RightButtonUp = 0x0010;
    internal const uint WindowKeyDown = 0x0100;
    internal const uint WindowKeyUp = 0x0101;
    internal const uint WindowMouseMove = 0x0200;
    internal const uint WindowRightButtonDown = 0x0204;
    internal const uint WindowRightButtonUp = 0x0205;
    internal const int MouseKeyRightButton = 0x0002;
    internal const uint SendMessageAbortIfHung = 0x0002;
    internal const uint StillActive = 259;

    internal static void EnableDebugPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not open the current process token.");
        try
        {
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not resolve SeDebugPrivilege.");
            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privileges = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled }
            };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable SeDebugPrivilege.");
            var error = Marshal.GetLastWin32Error();
            if (error != 0) throw new Win32Exception(error, "SeDebugPrivilege was not assigned; run elevated.");
        }
        finally { CloseHandle(token); }
    }

    internal static bool IsProcessForeground(int processId)
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero && GetWindowThreadProcessId(foreground, out var pid) != 0 && pid == processId;
    }

    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, nuint size, out nuint bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Module32First(IntPtr snapshot, ref ModuleEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool Module32Next(IntPtr snapshot, ref ModuleEntry32 entry);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges, ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClipCursor(out Rect rect);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCursorClip(ref Rect rect);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ReleaseCursorClip(IntPtr rect);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool MessageBeep(uint type);
    [DllImport("user32.dll")] internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKey(uint code, uint mapType);
}

[StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X; internal int Y; }
[StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }
[StructLayout(LayoutKind.Sequential)] internal struct Luid { internal uint LowPart; internal int HighPart; }
[StructLayout(LayoutKind.Sequential)] internal struct LuidAndAttributes { internal Luid Luid; internal uint Attributes; }
[StructLayout(LayoutKind.Sequential)] internal struct TokenPrivileges { internal uint PrivilegeCount; internal LuidAndAttributes Privileges; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ModuleEntry32
{
    internal uint dwSize, th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
    internal IntPtr modBaseAddr;
    internal uint modBaseSize;
    internal IntPtr hModule;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string szModule;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string szExePath;
}
