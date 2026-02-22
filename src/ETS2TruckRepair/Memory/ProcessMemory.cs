using ETS2TruckRepair.Native;

namespace ETS2TruckRepair.Memory;

/// <summary>
/// Provides high-level memory read/write operations for a process
/// </summary>
public sealed class ProcessMemory : IDisposable
{
    private readonly nint _processHandle;
    private readonly nint _moduleBase;
    private bool _disposed;

    public nint ModuleBase => _moduleBase;
    public nint ProcessHandle => _processHandle;
    public bool IsValid => _processHandle != nint.Zero;

    public ProcessMemory(int processId, nint moduleBase)
    {
        _moduleBase = moduleBase;
        _processHandle = Kernel32.OpenProcess(
            ProcessAccessFlags.AllForMemory,
            false,
            processId);

        if (_processHandle == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Nie można otworzyć procesu (PID: {processId}). Uruchom jako Administrator!");
        }
    }

    /// <summary>
    /// Reads a 64-bit pointer from memory
    /// </summary>
    public nint ReadPointer(nint address)
    {
        var buffer = new byte[8]; // 64-bit pointer
        if (!Kernel32.ReadProcessMemory(_processHandle, address, buffer, 8, out _))
        {
            return nint.Zero;
        }
        return (nint)BitConverter.ToInt64(buffer);
    }

    /// <summary>
    /// Reads a float value from memory
    /// </summary>
    public float ReadFloat(nint address)
    {
        var buffer = new byte[4];
        if (!Kernel32.ReadProcessMemory(_processHandle, address, buffer, 4, out _))
        {
            return float.NaN;
        }
        return BitConverter.ToSingle(buffer);
    }

    /// <summary>
    /// Writes a float value to memory
    /// </summary>
    public bool WriteFloat(nint address, float value)
    {
        var buffer = BitConverter.GetBytes(value);
        return Kernel32.WriteProcessMemory(_processHandle, address, buffer, 4, out _);
    }

    /// <summary>
    /// Resolves a multi-level pointer chain
    /// Example: [[moduleBase + offset1] + offset2] + offset3
    /// </summary>
    public nint ResolvePointerChain(params int[] offsets)
    {
        if (offsets.Length == 0) return nint.Zero;

        // Start from module base + first offset
        var address = _moduleBase + offsets[0];

        // Follow pointer chain (all except last offset)
        for (int i = 1; i < offsets.Length; i++)
        {
            address = ReadPointer(address);
            if (address == nint.Zero) return nint.Zero;
            address += offsets[i];
        }

        return address;
    }

    /// <summary>
    /// Reads a block of bytes from process memory
    /// </summary>
    public byte[]? ReadBytes(nint address, int size)
    {
        var buffer = new byte[size];
        if (!Kernel32.ReadProcessMemory(_processHandle, address, buffer, (nuint)size, out var bytesRead))
            return null;
        if ((int)bytesRead < size)
            Array.Resize(ref buffer, (int)bytesRead);
        return buffer;
    }

    /// <summary>
    /// Reads a 32-bit integer from memory
    /// </summary>
    public int ReadInt32(nint address)
    {
        var buffer = new byte[4];
        if (!Kernel32.ReadProcessMemory(_processHandle, address, buffer, 4, out _))
            return 0;
        return BitConverter.ToInt32(buffer);
    }

    public void Dispose()
    {
        if (!_disposed && _processHandle != nint.Zero)
        {
            Kernel32.CloseHandle(_processHandle);
            _disposed = true;
        }
    }
}
