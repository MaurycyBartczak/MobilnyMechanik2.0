using System.Runtime.InteropServices;
using ETS2TruckRepair.Native;

namespace ETS2TruckRepair.Scanning;

/// <summary>
/// Extracts AOB (Array of Bytes) patterns from executable code that references
/// a known static address via RIP-relative addressing.
/// </summary>
public sealed class AobExtractor
{
    private const int ChunkSize = 256 * 1024;
    private readonly nint _processHandle;

    public AobExtractor(nint processHandle)
    {
        _processHandle = processHandle;
    }

    public record AobResult(nint InstructionAddress, byte[] Bytes, int OperandOffset, int InstructionLength);

    /// <summary>
    /// Finds instructions in the .text section that use RIP-relative addressing
    /// to reference the given static address.
    ///
    /// Common patterns:
    ///   48 8B 05 XX XX XX XX    mov rax, [rip+disp32]     (7 bytes)
    ///   48 8B 0D XX XX XX XX    mov rcx, [rip+disp32]     (7 bytes)
    ///   48 8B 15 XX XX XX XX    mov rdx, [rip+disp32]     (7 bytes)
    ///   48 8B 35 XX XX XX XX    mov rsi, [rip+disp32]     (7 bytes)
    ///   48 8B 3D XX XX XX XX    mov rdi, [rip+disp32]     (7 bytes)
    ///   4C 8B 05 XX XX XX XX    mov r8, [rip+disp32]      (7 bytes)
    ///   48 89 05 XX XX XX XX    mov [rip+disp32], rax     (7 bytes)
    ///   48 8D 05 XX XX XX XX    lea rax, [rip+disp32]     (7 bytes)
    /// </summary>
    public List<AobResult> FindRipRelativeReferences(nint targetStaticAddr, nint moduleBase, int moduleSize)
    {
        var results = new List<AobResult>();
        var address = moduleBase;
        var moduleEnd = moduleBase + moduleSize;

        while (address < moduleEnd)
        {
            var queryResult = Kernel32.VirtualQueryEx(
                _processHandle, address,
                out var memInfo,
                (nuint)Marshal.SizeOf<MemoryBasicInformation>());

            if (queryResult == 0) break;

            var regionEnd = memInfo.BaseAddress + (nint)memInfo.RegionSize;

            // Only scan executable regions (.text section)
            if (memInfo.State == MemoryConstants.MemCommit && IsExecutable(memInfo.Protect))
            {
                ScanRegion(memInfo.BaseAddress, (int)memInfo.RegionSize, targetStaticAddr, results);
            }

            address = regionEnd;
            if (address <= memInfo.BaseAddress) break;
        }

        return results;
    }

    private void ScanRegion(nint baseAddr, int regionSize, nint target, List<AobResult> results)
    {
        int offset = 0;

        while (offset < regionSize)
        {
            int readSize = Math.Min(ChunkSize + 16, regionSize - offset); // overlap for boundary
            if (readSize < 7) break;

            var buffer = new byte[readSize];
            if (!Kernel32.ReadProcessMemory(_processHandle, baseAddr + offset, buffer, (nuint)readSize, out var bytesRead))
            {
                offset += ChunkSize;
                continue;
            }

            int actualRead = (int)bytesRead;

            // Search for RIP-relative MOV/LEA instructions with REX.W prefix
            // Pattern: [48|4C] [8B|8D|89] [ModRM with mod=00, rm=101] [disp32]
            for (int i = 0; i <= actualRead - 7; i++)
            {
                byte rex = buffer[i];
                if (rex != 0x48 && rex != 0x4C) continue;

                byte opcode = buffer[i + 1];
                if (opcode != 0x8B && opcode != 0x8D && opcode != 0x89) continue;

                byte modrm = buffer[i + 2];
                // mod=00 (bits 7-6 = 00), rm=101 (bits 2-0 = 101) = RIP-relative
                if ((modrm & 0xC7) != 0x05) continue;

                // Read the 32-bit displacement at offset i+3
                int disp = BitConverter.ToInt32(buffer, i + 3);

                // RIP-relative: target = instruction_end + displacement
                // instruction_end = baseAddr + offset + i + 7 (7-byte instruction)
                nint instructionEnd = baseAddr + offset + i + 7;
                nint resolved = instructionEnd + disp;

                if (resolved == target)
                {
                    // Found! Read surrounding bytes for a wider AOB pattern
                    int patternStart = Math.Max(0, i - 4);
                    int patternEnd = Math.Min(actualRead, i + 7 + 8);
                    int patternLen = patternEnd - patternStart;

                    var patternBytes = new byte[patternLen];
                    Array.Copy(buffer, patternStart, patternBytes, 0, patternLen);

                    // The operand is at offset (i - patternStart + 3) within the pattern
                    int operandInPattern = (i - patternStart) + 3;

                    results.Add(new AobResult(
                        baseAddr + offset + i,
                        patternBytes,
                        operandInPattern,
                        7));
                }
            }

            offset += ChunkSize;
        }
    }

    /// <summary>
    /// Converts raw bytes + known operand position into a pattern string with wildcards.
    /// The disp32 operand becomes ?? wildcards since it changes with ASLR.
    /// </summary>
    public static string ToPatternString(byte[] bytes, int operandOffset, int operandSize = 4)
    {
        var parts = new string[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i >= operandOffset && i < operandOffset + operandSize)
                parts[i] = "??";
            else
                parts[i] = bytes[i].ToString("X2");
        }
        return string.Join(" ", parts);
    }

    private static bool IsExecutable(uint protect) =>
        protect is MemoryConstants.PageExecuteRead or MemoryConstants.PageExecuteReadWrite;
}
