using System.Diagnostics;
using ETS2TruckRepair.Memory;
using ETS2TruckRepair.Native;
using ETS2TruckRepair.Scanning;

namespace ETS2TruckRepair.Repair;

/// <summary>
/// Repairs ETS2 truck and trailer damage using code injection + NOP patching.
/// 1. Captures vehicle pointer via trampoline
/// 2. Zeros all damage/wear values
/// 3. NOPs out movss store instructions in wear function to prevent re-application
/// 4. Restores original code on Dispose
/// </summary>
public sealed class RepairEngine : IDisposable
{
    // AOB patterns from the CE table
    // Pierwszy bajt jako ?? — moze byc oryginalny (40) albo juz zpatchowany (C3/RET)
    private const string TruckWearAOB = "?? 55 56 57 48 83 EC 70 48 8B B1 48 01 00 00 0F 57 E4";
    private const string TruckDamageAOB = "48 83 EC 18 4C 8B 81 48 01 00 00";
    private const string TrailerWearAOB = "?? 55 41 56 41 57 48 81 EC 80 00 00 00 48 8B A9 48 01 00 00";
    private const string TrailerDamageAOB = "48 8B 81 48 01 00 00 0F 57 ED";

    private const int BodyDataOffset = 0x148;

    // Memory allocation constants
    private const uint MemCommitReserve = 0x1000 | 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageExecuteReadWrite = 0x40;

    // How many bytes of the wear function to disassemble for offset extraction
    private const int FuncScanSize = 2048;

    private ProcessMemory? _memory;
    private Process? _process;
    private nint _moduleBase;
    private int _moduleSize;

    // Saved patches for restoration on Dispose
    private readonly List<(nint address, byte[] originalBytes)> _patches = new();

    // Cached pointers for continuous wheel repair
    private nint _lastVehiclePtr;
    private HashSet<int> _lastDirectOffsets = new();

    public bool AttachToGame()
    {
        var processes = Process.GetProcessesByName("eurotrucks2");
        if (processes.Length == 0) return false;

        _process = processes[0];

        var (moduleBase, moduleSize) = ModuleHelper.GetModuleInfo(_process);
        if (moduleBase == nint.Zero) return false;

        _moduleBase = moduleBase;
        _moduleSize = moduleSize;
        _memory = new ProcessMemory(_process.Id, moduleBase);

        Console.WriteLine($"  Proces: {_process.ProcessName} (PID: {_process.Id})");
        Console.WriteLine($"  Modul: 0x{moduleBase:X} ({moduleSize / 1024}KB)");

        return _memory.IsValid;
    }

    public bool RepairTruck()
    {
        if (_memory is null) return false;

        Console.WriteLine("\n  [Ciezarowka] Szukam funkcji wear...");
        var wearFunc = FindFunction(TruckWearAOB);
        if (wearFunc == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  [Ciezarowka] Nie znaleziono funkcji wear.");
            Console.ResetColor();
            return false;
        }

        Console.WriteLine($"  [Ciezarowka] Funkcja wear: 0x{wearFunc:X}");

        // Sprawdz czy wear function jest juz zpatchowana (RET)
        bool alreadyPatched = false;
        var firstByte = _memory.ReadBytes(wearFunc, 1);
        if (firstByte != null && firstByte[0] == 0xC3)
        {
            alreadyPatched = true;
            Console.WriteLine("  [Ciezarowka] Wear juz zablokowany — tymczasowo przywracam do capture...");
            // Przywroc oryginalny bajt (0x40 = push rbp z REX prefix) zeby funkcja dzialala podczas capture
            RestoreFunctionFromRet(wearFunc, 0x40);
        }

        // Extract wear offsets (for zeroing current values)
        var (directOffsets, arrayBases) = ExtractWriteOffsets(wearFunc, 0x06); // RSI = rm 110 = 6
        Console.WriteLine($"  [Ciezarowka] Offsety bezposrednie ({directOffsets.Count}): {string.Join(", ", directOffsets.Order().Select(o => $"0x{o:X}"))}");

        // Capture vehicle pointer
        var vehiclePtr = TryCaptureFromFunction("Ciezarowka", "wear", TruckWearAOB, 15);

        // Niezaleznie od wyniku capture — znow daj RET
        PatchFunctionToRet(wearFunc, "wear");

        if (vehiclePtr == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  [Ciezarowka] Nie udalo sie przechwycic wskaznika.");
            Console.ResetColor();
            return false;
        }

        Console.WriteLine($"  [Ciezarowka] Obiekt pojazdu: 0x{vehiclePtr:X}");

        // Cache for continuous wheel repair
        _lastVehiclePtr = vehiclePtr;
        _lastDirectOffsets = directOffsets;

        // 1. Zero current damage values
        bool ok = RepairAtOffsets(vehiclePtr, directOffsets, arrayBases, "Ciezarowka");

        // 2. Zero wheel values
        var bodyPtr = _memory.ReadPointer(vehiclePtr + BodyDataOffset);
        if (bodyPtr != nint.Zero)
        {
            int wheelZeroed = RepairWheelRange(bodyPtr, directOffsets, 0x100, 0x158, "Ciezarowka");
            wheelZeroed += RepairWheelRange(bodyPtr, directOffsets, 0x300, 0x380, "Ciezarowka");
            if (wheelZeroed > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  [Ciezarowka] Kola: wyzerowano {wheelZeroed} wartosci.");
                Console.ResetColor();
                ok = true;
            }
        }

        return ok;
    }

    public bool RepairTrailer()
    {
        if (_memory is null) return false;

        Console.WriteLine("\n  [Naczepa] Szukam funkcji wear...");
        var wearFunc = FindFunction(TrailerWearAOB);

        if (wearFunc == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [Naczepa] Nie znaleziono funkcji wear.");
            Console.ResetColor();
            return false;
        }

        Console.WriteLine($"  [Naczepa] Funkcja wear: 0x{wearFunc:X}");
        var (trailerDirect, trailerArrays) = ExtractWriteOffsets(wearFunc, 0x05); // RBP = rm 101 = 5
        var storeInstructions = ExtractStoreInstructions(wearFunc, 0x05);

        Console.WriteLine($"  [Naczepa] Offsety ({trailerDirect.Count} + {trailerArrays.Count} tablic), stores: {storeInstructions.Count}");

        var trailerPtr = TryCaptureFromFunction("Naczepa", "wear", TrailerWearAOB, 20);

        if (trailerPtr == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [Naczepa] Nie przechwycono - moze nie masz podpietej naczepy.");
            Console.ResetColor();
            return false;
        }

        Console.WriteLine($"  [Naczepa] Obiekt naczepy: 0x{trailerPtr:X}");
        bool ok = RepairAtOffsets(trailerPtr, trailerDirect, trailerArrays, "Naczepa");

        // NOP out trailer wear stores
        if (storeInstructions.Count > 0)
        {
            int nopCount = NopStoreInstructions(wearFunc, storeInstructions);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  [Naczepa] Zablokowano {nopCount} instrukcji zapisu wear (NOP).");
            Console.ResetColor();
        }

        return ok;
    }

    /// <summary>
    /// Zeruje koła i wartości wear/damage — wywoływane cyklicznie w pętli.
    /// Cicho (bez logów) zeruje wszystkie wartości w znanych zakresach.
    /// </summary>
    public int RepairWheelsTick()
    {
        if (_memory is null || _lastVehiclePtr == nint.Zero) return 0;

        var bodyPtr = _memory.ReadPointer(_lastVehiclePtr + BodyDataOffset);
        if (bodyPtr == nint.Zero) return 0;

        int zeroed = 0;

        // Zero known direct offsets (wear function fields)
        foreach (var offset in _lastDirectOffsets)
        {
            float val = _memory.ReadFloat(bodyPtr + offset);
            if (!float.IsNaN(val) && val > 0.0f)
            {
                _memory.WriteFloat(bodyPtr + offset, 0.0f);
                zeroed++;
            }
        }

        // Zero wheel ranges
        zeroed += SilentZeroRange(bodyPtr, 0x100, 0x158);
        zeroed += SilentZeroRange(bodyPtr, 0x300, 0x380);

        return zeroed;
    }

    private int SilentZeroRange(nint bodyPtr, int startOff, int endOff)
    {
        if (_memory is null) return 0;

        int size = endOff - startOff;
        var data = _memory.ReadBytes(bodyPtr + startOff, size);
        if (data is null) return 0;

        int zeroed = 0;
        for (int i = 0; i <= data.Length - 4; i += 4)
        {
            float val = BitConverter.ToSingle(data, i);
            if (val > 0.001f && val <= 1.0f)
            {
                if (i + 8 <= data.Length)
                {
                    uint upperHalf = BitConverter.ToUInt32(data, i + 4);
                    if (upperHalf > 0 && upperHalf < 0x8000)
                        continue;
                }

                _memory.WriteFloat(bodyPtr + (startOff + i), 0.0f);
                zeroed++;
            }
        }

        return zeroed;
    }

    /// <summary>
    /// Przywraca oryginalny pierwszy bajt funkcji (usuwajac RET patch).
    /// </summary>
    private void RestoreFunctionFromRet(nint funcAddr, byte originalByte)
    {
        if (_memory is null) return;

        Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, 1,
            PageExecuteReadWrite, out uint oldProtect);
        WriteBytes(funcAddr, new byte[] { originalByte });
        Kernel32.FlushInstructionCache(_memory.ProcessHandle, funcAddr, 1);
        Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, 1, oldProtect, out _);
    }

    /// <summary>
    /// Zamienia poczatek funkcji na RET (0xC3).
    /// Cala funkcja przestaje dzialac — zadne zapisy wear/damage nie sa wykonywane.
    /// </summary>
    private void PatchFunctionToRet(nint funcAddr, string funcName)
    {
        if (_memory is null) return;

        // Zapisz oryginalny bajt
        var original = _memory.ReadBytes(funcAddr, 1);
        if (original is null) return;

        _patches.Add((funcAddr, original));

        if (!Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, 1,
            PageExecuteReadWrite, out uint oldProtect))
            return;

        // Wstaw RET
        WriteBytes(funcAddr, new byte[] { 0xC3 });
        Kernel32.FlushInstructionCache(_memory.ProcessHandle, funcAddr, 1);
        Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, 1, oldProtect, out _);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [Ciezarowka] Funkcja {funcName} zablokowana (RET).");
        Console.ResetColor();
    }

    /// <summary>
    /// Restores all NOP-patched instructions to their originals.
    /// Call this before exiting.
    /// </summary>
    public void RestorePatches()
    {
        if (_memory is null) return;

        foreach (var (address, original) in _patches)
        {
            Kernel32.VirtualProtectEx(_memory.ProcessHandle, address, (nuint)original.Length,
                PageExecuteReadWrite, out uint oldProtect);
            WriteBytes(address, original);
            Kernel32.FlushInstructionCache(_memory.ProcessHandle, address, (nuint)original.Length);
            Kernel32.VirtualProtectEx(_memory.ProcessHandle, address, (nuint)original.Length,
                oldProtect, out _);
        }

        if (_patches.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n  Przywrocono {_patches.Count} oryginalnych instrukcji.");
            Console.ResetColor();
        }

        _patches.Clear();
    }

    /// <summary>
    /// Extracts all offsets accessed by movss/addss instructions in the wear function.
    /// </summary>
    private (HashSet<int> direct, HashSet<int> arrayBases) ExtractWriteOffsets(nint funcAddr, int baseRegRm)
    {
        if (_memory is null) return (new(), new());

        var code = _memory.ReadBytes(funcAddr, FuncScanSize);
        if (code is null) return (new(), new());

        var directOffsets = new HashSet<int>();
        var arrayBaseOffsets = new HashSet<int>();

        for (int i = 0; i < code.Length - 10; i++)
        {
            int pos = i;

            if (code[pos] != 0xF3) continue;
            pos++;

            if (pos < code.Length && (code[pos] & 0xF0) == 0x40)
                pos++;

            if (pos + 1 >= code.Length || code[pos] != 0x0F) continue;
            byte opcode2 = code[pos + 1];
            if (opcode2 != 0x10 && opcode2 != 0x11 && opcode2 != 0x58 && opcode2 != 0x59 && opcode2 != 0x5C) continue;
            pos += 2;

            if (pos >= code.Length) continue;
            byte modrm = code[pos];
            pos++;

            int mod = (modrm >> 6) & 3;
            int rm = modrm & 7;

            if (rm == baseRegRm && rm != 4)
            {
                if (mod == 1 && pos < code.Length)
                {
                    int offset = (sbyte)code[pos];
                    if (offset >= 0x20) directOffsets.Add(offset);
                }
                else if (mod == 2 && pos + 3 < code.Length)
                {
                    int offset = BitConverter.ToInt32(code, pos);
                    if (offset >= 0x20 && offset < 0x10000) directOffsets.Add(offset);
                }
            }
            else if (rm == 4 && pos < code.Length)
            {
                byte sib = code[pos];
                pos++;
                int sibBase = sib & 7;

                if (sibBase == baseRegRm)
                {
                    if (mod == 1 && pos < code.Length)
                    {
                        int offset = (sbyte)code[pos];
                        if (offset >= 0x20) arrayBaseOffsets.Add(offset);
                    }
                    else if (mod == 2 && pos + 3 < code.Length)
                    {
                        int offset = BitConverter.ToInt32(code, pos);
                        if (offset >= 0x20 && offset < 0x10000) arrayBaseOffsets.Add(offset);
                    }
                }
            }
        }

        return (directOffsets, arrayBaseOffsets);
    }

    /// <summary>
    /// Extracts positions and sizes of movss STORE instructions (0F 11) that write to [baseReg+disp].
    /// These are the instructions we'll NOP out.
    /// </summary>
    private List<(int offset, int length)> ExtractStoreInstructions(nint funcAddr, int baseRegRm)
    {
        if (_memory is null) return new();

        var code = _memory.ReadBytes(funcAddr, FuncScanSize);
        if (code is null) return new();

        var instructions = new List<(int offset, int length)>();

        for (int i = 0; i < code.Length - 10; i++)
        {
            int startPos = i;
            int pos = i;

            if (code[pos] != 0xF3) continue;
            pos++;

            // Optional REX prefix
            bool hasRex = pos < code.Length && (code[pos] & 0xF0) == 0x40;
            if (hasRex) pos++;

            if (pos + 1 >= code.Length || code[pos] != 0x0F) continue;
            byte opcode2 = code[pos + 1];
            // Only stores: 0F 11 = movss store
            if (opcode2 != 0x11) continue;
            pos += 2;

            if (pos >= code.Length) continue;
            byte modrm = code[pos];
            pos++;

            int mod = (modrm >> 6) & 3;
            int rm = modrm & 7;

            bool isTargetReg = false;
            if (rm == baseRegRm && rm != 4)
            {
                isTargetReg = true;
            }
            else if (rm == 4 && pos < code.Length)
            {
                byte sib = code[pos];
                int sibBase = sib & 7;
                if (sibBase == baseRegRm)
                    isTargetReg = true;
                pos++; // skip SIB
            }

            if (!isTargetReg) continue;

            // Calculate displacement size
            if (mod == 1)
                pos += 1; // 8-bit displacement
            else if (mod == 2)
                pos += 4; // 32-bit displacement

            int instrLength = pos - startPos;
            instructions.Add((startPos, instrLength));
        }

        return instructions;
    }

    /// <summary>
    /// Extracts ALL movss store instructions in a function (any register target).
    /// Used for damage function where we don't know which register holds body ptr.
    /// Only includes stores with displacement >= 0x20 (skip vtable/small offsets).
    /// </summary>
    private List<(int offset, int length)> ExtractAllStoreInstructions(nint funcAddr)
    {
        if (_memory is null) return new();

        var code = _memory.ReadBytes(funcAddr, FuncScanSize);
        if (code is null) return new();

        var instructions = new List<(int offset, int length)>();

        for (int i = 0; i < code.Length - 10; i++)
        {
            int startPos = i;
            int pos = i;

            if (code[pos] != 0xF3) continue;
            pos++;

            if (pos < code.Length && (code[pos] & 0xF0) == 0x40)
                pos++;

            if (pos + 1 >= code.Length || code[pos] != 0x0F) continue;
            if (code[pos + 1] != 0x11) continue; // only stores
            pos += 2;

            if (pos >= code.Length) continue;
            byte modrm = code[pos];
            pos++;

            int mod = (modrm >> 6) & 3;
            int rm = modrm & 7;

            if (mod == 0 || mod == 3) continue; // skip [reg] without disp and register-only

            if (rm == 4 && pos < code.Length)
                pos++; // skip SIB

            int displacement = 0;
            if (mod == 1 && pos < code.Length)
            {
                displacement = (sbyte)code[pos];
                pos += 1;
            }
            else if (mod == 2 && pos + 3 < code.Length)
            {
                displacement = BitConverter.ToInt32(code, pos);
                pos += 4;
            }

            // Only NOP stores to "interesting" offsets (skip very low / very high)
            if (displacement < 0x20 || displacement > 0x10000) continue;

            // Stop at RET instruction (0xC3) - don't scan past function end
            bool pastRet = false;
            for (int j = startPos - 1; j >= Math.Max(0, startPos - 30); j--)
            {
                if (code[j] == 0xC3) { pastRet = true; break; }
            }
            if (pastRet) break;

            int instrLength = pos - startPos;
            instructions.Add((startPos, instrLength));
        }

        return instructions;
    }

    /// <summary>
    /// NOPs out store instructions at the given positions in the function.
    /// Saves original bytes for restoration.
    /// </summary>
    private int NopStoreInstructions(nint funcAddr, List<(int offset, int length)> instructions)
    {
        if (_memory is null) return 0;

        int count = 0;
        foreach (var (offset, length) in instructions)
        {
            nint instrAddr = funcAddr + offset;

            // Save original bytes
            var original = _memory.ReadBytes(instrAddr, length);
            if (original is null) continue;

            _patches.Add((instrAddr, original));

            // Make writable
            if (!Kernel32.VirtualProtectEx(_memory.ProcessHandle, instrAddr, (nuint)length,
                PageExecuteReadWrite, out uint oldProtect))
                continue;

            // Write NOPs
            var nops = new byte[length];
            Array.Fill(nops, (byte)0x90);
            if (WriteBytes(instrAddr, nops))
            {
                Kernel32.FlushInstructionCache(_memory.ProcessHandle, instrAddr, (nuint)length);
                count++;
            }

            // Restore protection
            Kernel32.VirtualProtectEx(_memory.ProcessHandle, instrAddr, (nuint)length,
                oldProtect, out _);
        }

        return count;
    }

    /// <summary>
    /// Zero wear/damage values at known offsets.
    /// </summary>
    private bool RepairAtOffsets(nint vehiclePtr, HashSet<int> directOffsets, HashSet<int> arrayBases, string label)
    {
        if (_memory is null) return false;

        var bodyPtr = _memory.ReadPointer(vehiclePtr + BodyDataOffset);
        if (bodyPtr == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{label}] [pojazd+0x148] = null - brak struktury damage.");
            Console.ResetColor();
            return false;
        }

        Console.WriteLine($"  [{label}] Struktura damage: 0x{bodyPtr:X}");

        int zeroed = 0;

        foreach (var offset in directOffsets.Order())
        {
            float val = _memory.ReadFloat(bodyPtr + offset);
            if (float.IsNaN(val)) continue;

            if (val > 0.0f)
            {
                if (_memory.WriteFloat(bodyPtr + offset, 0.0f))
                {
                    Console.WriteLine($"    0x{offset:X3}: {val:F4} -> 0.0");
                    zeroed++;
                }
            }
        }

        foreach (var baseOffset in arrayBases.Order())
        {
            for (int j = 0; j < 64; j += 4)
            {
                int off = baseOffset + j;
                float val = _memory.ReadFloat(bodyPtr + off);
                if (float.IsNaN(val)) continue;

                if (val > 0.001f && val <= 1.0f)
                {
                    if (_memory.WriteFloat(bodyPtr + off, 0.0f))
                    {
                        Console.WriteLine($"    0x{off:X3}: {val:F4} -> 0.0");
                        zeroed++;
                    }
                }
            }
        }

        Console.ForegroundColor = zeroed > 0 ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine($"  [{label}] Wyzerowano {zeroed} wartosci wear/damage.");
        Console.ResetColor();

        return zeroed > 0;
    }

    /// <summary>
    /// Scans a range of the body structure for wheel wear/damage floats (0.001-1.0).
    /// </summary>
    private int RepairWheelRange(nint bodyPtr, HashSet<int> skipOffsets, int startOff, int endOff, string label)
    {
        if (_memory is null) return 0;

        int size = endOff - startOff;
        var data = _memory.ReadBytes(bodyPtr + startOff, size);
        if (data is null) return 0;

        int zeroed = 0;
        for (int i = 0; i <= data.Length - 4; i += 4)
        {
            int offset = startOff + i;
            if (skipOffsets.Contains(offset)) continue;

            float val = BitConverter.ToSingle(data, i);
            if (val > 0.001f && val <= 1.0f)
            {
                if (i + 8 <= data.Length)
                {
                    uint upperHalf = BitConverter.ToUInt32(data, i + 4);
                    if (upperHalf > 0 && upperHalf < 0x8000)
                        continue;
                }

                if (_memory.WriteFloat(bodyPtr + offset, 0.0f))
                {
                    Console.WriteLine($"    [Kola] 0x{offset:X3}: {val:F4} -> 0.0");
                    zeroed++;
                }
            }
        }

        return zeroed;
    }

    private nint TryCaptureFromFunction(string label, string funcName, string aobPattern, int minPatchSize)
    {
        if (_memory is null) return nint.Zero;

        var funcAddr = FindFunction(aobPattern);
        if (funcAddr == nint.Zero) return nint.Zero;

        int patchSize = Math.Max(minPatchSize, 15);

        Console.WriteLine($"  [{label}] Przechwytywanie z {funcName} (patchSize={patchSize})...");

        return CaptureVehiclePointer(funcAddr, patchSize);
    }

    private nint FindFunction(string aobPattern)
    {
        if (_memory is null) return nint.Zero;

        var (pattern, mask) = PatternScanner.ParsePattern(aobPattern);
        var scanner = new PatternScanner(_memory.ProcessHandle);
        var matches = scanner.ScanModule(_moduleBase, _moduleSize, pattern, mask);

        return matches.Count > 0 ? matches[0] : nint.Zero;
    }

    private nint CaptureVehiclePointer(nint funcAddr, int patchSize)
    {
        if (_memory is null) return nint.Zero;

        var originalBytes = _memory.ReadBytes(funcAddr, patchSize);
        if (originalBytes is null || originalBytes.Length < patchSize) return nint.Zero;

        int caveSize = 128;
        var cave = Kernel32.VirtualAllocEx(
            _memory.ProcessHandle, nint.Zero, (nuint)caveSize,
            MemCommitReserve, PageExecuteReadWrite);

        if (cave == nint.Zero) return nint.Zero;

        try
        {
            var trampoline = BuildTrampoline(cave, funcAddr, originalBytes, patchSize);
            int captureVarOffset = 7 + patchSize + 6 + 8;

            if (!WriteBytes(cave, trampoline)) return nint.Zero;
            Kernel32.FlushInstructionCache(_memory.ProcessHandle, cave, (nuint)trampoline.Length);

            if (!Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, (nuint)patchSize,
                PageExecuteReadWrite, out uint oldProtect))
                return nint.Zero;

            var jumpPatch = BuildAbsoluteJump(cave, patchSize);
            if (!WriteBytes(funcAddr, jumpPatch)) return nint.Zero;
            Kernel32.FlushInstructionCache(_memory.ProcessHandle, funcAddr, (nuint)patchSize);

            // Wait for the function to be called (up to ~10 seconds)
            Console.Write("    Czekam (max 10s)");
            nint capturedRcx = nint.Zero;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                Thread.Sleep(200);
                capturedRcx = _memory.ReadPointer(cave + captureVarOffset);
                if (attempt % 5 == 0) Console.Write(".");
                if (capturedRcx != nint.Zero) break;
            }
            Console.WriteLine();

            // Restore original code
            Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, (nuint)patchSize,
                PageExecuteReadWrite, out oldProtect);
            WriteBytes(funcAddr, originalBytes);
            Kernel32.FlushInstructionCache(_memory.ProcessHandle, funcAddr, (nuint)patchSize);
            Kernel32.VirtualProtectEx(_memory.ProcessHandle, funcAddr, (nuint)patchSize,
                oldProtect, out _);

            return capturedRcx;
        }
        finally
        {
            Kernel32.VirtualFreeEx(_memory.ProcessHandle, cave, 0, MemRelease);
        }
    }

    private static byte[] BuildTrampoline(nint caveAddr, nint funcAddr, byte[] originalBytes, int patchSize)
    {
        int movEnd = 7;
        int origEnd = movEnd + patchSize;
        int jmpEnd = origEnd + 6;
        int addrEnd = jmpEnd + 8;
        int captureOffset = addrEnd;
        int totalSize = captureOffset + 8;

        var code = new byte[totalSize];

        int disp = captureOffset - movEnd;
        code[0] = 0x48; code[1] = 0x89; code[2] = 0x0D;
        BitConverter.GetBytes(disp).CopyTo(code, 3);

        Array.Copy(originalBytes, 0, code, movEnd, patchSize);

        code[origEnd] = 0xFF;
        code[origEnd + 1] = 0x25;

        BitConverter.GetBytes((long)(funcAddr + patchSize)).CopyTo(code, jmpEnd);

        return code;
    }

    private static byte[] BuildAbsoluteJump(nint target, int patchSize)
    {
        var patch = new byte[patchSize];
        patch[0] = 0xFF;
        patch[1] = 0x25;
        BitConverter.GetBytes((long)target).CopyTo(patch, 6);
        for (int i = 14; i < patchSize; i++)
            patch[i] = 0x90;
        return patch;
    }

    private bool WriteBytes(nint address, byte[] data)
    {
        if (_memory is null) return false;
        return Kernel32.WriteProcessMemory(
            _memory.ProcessHandle, address, data, (nuint)data.Length, out _);
    }

    public void Dispose()
    {
        // NIE przywracamy NOP-ów automatycznie - uszkodzenia nie wrócą dopóki gra działa.
        // Użyj RestorePatches() ręcznie jeśli chcesz przywrócić wear.
        _memory?.Dispose();
        _process?.Dispose();
    }
}
