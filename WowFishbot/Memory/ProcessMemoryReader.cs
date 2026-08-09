using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WowFishbot.Interop;

namespace WowFishbot.Memory;

internal sealed class ProcessMemoryReader : IDisposable
{
    private readonly IntPtr _handle;

    private ProcessMemoryReader(IntPtr handle, ProcessInfo info)
    {
        _handle = handle;
        Info = info;
    }

    internal ProcessInfo Info { get; }
    internal bool IsProcessAlive => NativeMethods.GetExitCodeProcess(_handle, out var exitCode) && exitCode == NativeMethods.StillActive;

    internal static ProcessMemoryReader Open(int? requestedPid, string processName)
    {
        using var process = requestedPid is not null
            ? Process.GetProcessById(requestedPid.Value)
            : Process.GetProcessesByName(processName).OrderByDescending(item => item.StartTime).FirstOrDefault()
              ?? throw new InvalidOperationException($"Process '{processName}' was not found.");

        const uint access = NativeMethods.ProcessVmRead | NativeMethods.ProcessQueryInformation;
        var handle = NativeMethods.OpenProcess(access, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"Could not open PID {process.Id} with read-only access 0x{access:X}.");
        }

        try
        {
            ModuleInfo module;
            try
            {
                module = FindMainModule(process.Id, process.ProcessName + ".exe");
            }
            catch
            {
                module = ReadPeModuleFallback(handle, ClientOffsets.PreferredImageBase, process.ProcessName + ".exe");
            }
            return new ProcessMemoryReader(handle, new ProcessInfo(process.Id, module.BaseAddress));
        }
        catch
        {
            NativeMethods.CloseHandle(handle);
            throw;
        }
    }

    internal bool TryGetLocalPlayer(out PlayerReference player)
    {
        player = default!;
        if (!TryGetObjectManager(out var manager) ||
            !TryReadUInt64(manager + ClientOffsets.LocalGuid, out var guid) || guid == 0 ||
            !TryFindObjectByGuid(guid, requiredType: 4, out var objectReference))
            return false;
        player = new PlayerReference(objectReference.ObjectAddress, guid);
        return true;
    }

    internal IReadOnlyList<OwnedFishingBobber> FindOwnedFishingBobbers(ulong ownerGuid)
    {
        var found = new List<OwnedFishingBobber>(2);
        if (ownerGuid == 0 || !TryGetFirstObject(out var current)) return found;

        var firstObject = current;
        for (var i = 0; i < 4096 && IsValidPointer(current); i++)
        {
            if (!TryReadUInt16(current + ClientOffsets.ObjectType, out var objectType) || objectType > 7) break;
            TryReadUInt64(current + ClientOffsets.ObjectGuid, out var objectGuid);
            if (objectType == 5 &&
                TryReadUInt32(current + ClientOffsets.Descriptors, out var descriptors) && IsValidPointer(descriptors) &&
                TryReadUInt64(descriptors, out var descriptorGuid) && descriptorGuid == objectGuid &&
                TryReadUInt32(descriptors + 3 * 4, out var entry) && entry == 35591 &&
                TryReadUInt64(descriptors + 6 * 4, out var confirmedOwner) && confirmedOwner == ownerGuid)
                found.Add(new OwnedFishingBobber(current, objectGuid));

            if (!TryReadUInt32(current + ClientOffsets.NextObject, out var next) || next == current || next == firstObject) break;
            current = next;
        }
        return found;
    }

    internal bool TryFindObjectByGuid(ulong guid, ushort requiredType, out ManagedObjectReference result)
    {
        result = default!;
        if (guid == 0 || !TryGetFirstObject(out var current)) return false;
        var firstObject = current;
        for (var i = 0; i < 4096 && IsValidPointer(current); i++)
        {
            if (!TryReadUInt16(current + ClientOffsets.ObjectType, out var objectType) || objectType > 7) return false;
            if (objectType == requiredType &&
                TryReadUInt64(current + ClientOffsets.ObjectGuid, out var objectGuid) && objectGuid == guid &&
                TryReadUInt32(current + ClientOffsets.Descriptors, out var descriptors) && IsValidPointer(descriptors) &&
                TryReadUInt64(descriptors, out var descriptorGuid) && descriptorGuid == guid)
            {
                TryReadUInt32(descriptors + 3 * 4, out var entry);
                result = new ManagedObjectReference(current, entry);
                return true;
            }
            if (!TryReadUInt32(current + ClientOffsets.NextObject, out var next) || next == current || next == firstObject) return false;
            current = next;
        }
        return false;
    }

    internal bool TryReadUInt16(uint address, out ushort value)
    {
        var bytes = new byte[2];
        value = 0;
        if (!TryRead(address, bytes)) return false;
        value = BitConverter.ToUInt16(bytes);
        return true;
    }

    internal bool TryReadUInt32(uint address, out uint value)
    {
        var bytes = new byte[4];
        value = 0;
        if (!TryRead(address, bytes)) return false;
        value = BitConverter.ToUInt32(bytes);
        return true;
    }

    internal bool TryReadUInt64(uint address, out ulong value)
    {
        var bytes = new byte[8];
        value = 0;
        if (!TryRead(address, bytes)) return false;
        value = BitConverter.ToUInt64(bytes);
        return true;
    }

    internal bool TryReadSingle(uint address, out float value)
    {
        var bytes = new byte[4];
        value = 0;
        if (!TryRead(address, bytes)) return false;
        value = BitConverter.ToSingle(bytes);
        return true;
    }

    internal bool TryReadBytes(uint address, int count, out byte[] bytes)
    {
        bytes = new byte[count];
        return TryRead(address, bytes);
    }

    private bool TryGetObjectManager(out uint manager)
    {
        manager = 0;
        var root = checked(Info.ModuleBase + ClientOffsets.ClientConnectionRva);
        return TryReadUInt32(root, out var connection) && IsValidPointer(connection) &&
               TryReadUInt32(connection + ClientOffsets.CurrentManager, out manager) && IsValidPointer(manager);
    }

    private bool TryGetFirstObject(out uint firstObject)
    {
        firstObject = 0;
        return TryGetObjectManager(out var manager) && TryReadUInt32(manager + ClientOffsets.FirstObject, out firstObject);
    }

    private bool TryRead(uint address, byte[] buffer) =>
        NativeMethods.ReadProcessMemory(_handle, new IntPtr(address), buffer, (nuint)buffer.Length, out var read) &&
        read == (nuint)buffer.Length;

    internal static bool IsValidPointer(uint address) => address is >= 0x10000 and <= 0xFFF00000;

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) NativeMethods.CloseHandle(_handle);
    }

    private static ModuleInfo FindMainModule(int pid, string expectedName)
    {
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapModule | NativeMethods.Th32csSnapModule32, (uint)pid);
        if (snapshot == new IntPtr(unchecked((int)NativeMethods.InvalidHandleValue)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enumerate process modules.");
        try
        {
            var entry = new ModuleEntry32 { dwSize = (uint)Marshal.SizeOf<ModuleEntry32>() };
            if (!NativeMethods.Module32First(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do
            {
                if (entry.szModule.Equals(expectedName, StringComparison.OrdinalIgnoreCase) || entry.th32ModuleID == 1)
                    return new ModuleInfo(unchecked((uint)entry.modBaseAddr.ToInt64()), entry.modBaseSize);
            } while (NativeMethods.Module32Next(snapshot, ref entry));
            throw new InvalidOperationException("Main module was not found.");
        }
        finally { NativeMethods.CloseHandle(snapshot); }
    }

    private static ModuleInfo ReadPeModuleFallback(IntPtr handle, uint baseAddress, string moduleName)
    {
        var dos = new byte[64];
        if (!NativeMethods.ReadProcessMemory(handle, new IntPtr(baseAddress), dos, (nuint)dos.Length, out var dosRead) || dosRead != (nuint)dos.Length || dos[0] != 'M' || dos[1] != 'Z')
            throw new InvalidOperationException($"No readable MZ header for {moduleName} at 0x{baseAddress:X8}.");
        var peOffset = BitConverter.ToUInt32(dos, 0x3C);
        if (peOffset > 0x1000) throw new InvalidOperationException("Implausible PE header offset.");
        var pe = new byte[0x80];
        if (!NativeMethods.ReadProcessMemory(handle, new IntPtr(baseAddress + peOffset), pe, (nuint)pe.Length, out var peRead) || peRead != (nuint)pe.Length || pe[0] != 'P' || pe[1] != 'E')
            throw new InvalidOperationException("The PE header was unreadable or invalid.");
        if (BitConverter.ToUInt16(pe, 4) != 0x014c) throw new InvalidOperationException("The client is not a 32-bit x86 image.");
        var size = BitConverter.ToUInt32(pe, 24 + 56);
        if (size is < 0x100000 or > 0x10000000) throw new InvalidOperationException("Implausible PE image size.");
        return new ModuleInfo(baseAddress, size);
    }
}

internal sealed record ProcessInfo(int ProcessId, uint ModuleBase);
internal sealed record ModuleInfo(uint BaseAddress, uint Size);
internal sealed record PlayerReference(uint ObjectAddress, ulong Guid);
internal sealed record OwnedFishingBobber(uint ObjectAddress, ulong ObjectGuid);
internal sealed record ManagedObjectReference(uint ObjectAddress, uint Entry);
