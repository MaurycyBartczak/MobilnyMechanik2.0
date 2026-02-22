using System.Runtime.InteropServices;

namespace ETS2TruckRepair.Native;

[StructLayout(LayoutKind.Sequential)]
public struct MemoryBasicInformation
{
    public nint BaseAddress;
    public nint AllocationBase;
    public uint AllocationProtect;
    public ushort PartitionId;
    public nuint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}

/// <summary>
/// P/Invoke definitions for Windows kernel32.dll functions
/// </summary>
public static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(
        ProcessAccessFlags dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        [Out] byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteProcessMemory(
        nint hProcess,
        nint lpBaseAddress,
        byte[] lpBuffer,
        nuint nSize,
        out nuint lpNumberOfBytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint VirtualQueryEx(
        nint hProcess,
        nint lpAddress,
        out MemoryBasicInformation lpBuffer,
        nuint dwLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint VirtualAllocEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint flAllocationType,
        uint flProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualFreeEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint dwFreeType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualProtectEx(
        nint hProcess,
        nint lpAddress,
        nuint dwSize,
        uint flNewProtect,
        out uint lpflOldProtect);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushInstructionCache(
        nint hProcess,
        nint lpBaseAddress,
        nuint dwSize);
}
