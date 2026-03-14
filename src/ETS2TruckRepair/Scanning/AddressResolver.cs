using ETS2TruckRepair.Config;
using ETS2TruckRepair.Memory;

namespace ETS2TruckRepair.Scanning;

/// <summary>
/// Extracts the target data address from a matched instruction address.
/// </summary>
public sealed class AddressResolver
{
    private readonly ProcessMemory _memory;

    public AddressResolver(ProcessMemory memory)
    {
        _memory = memory;
    }

    /// <summary>
    /// Given a matched instruction address and extraction config,
    /// resolves the runtime address of the damage data structure.
    /// </summary>
    public nint Resolve(nint matchAddress, PatternSet config)
    {
        nint baseAddr = config.ExtractionMethod switch
        {
            "rip_relative" => ResolveRipRelative(matchAddress, config),
            "embedded_offset" => ResolveEmbeddedOffset(matchAddress, config),
            _ => nint.Zero
        };

        if (baseAddr == nint.Zero) return nint.Zero;

        // For RIP-relative: baseAddr is the address of a global pointer variable.
        // We need to dereference it to get the actual object pointer.
        if (config.ExtractionMethod == "rip_relative")
        {
            baseAddr = _memory.ReadPointer(baseAddr);
            if (baseAddr == nint.Zero) return nint.Zero;
        }

        // Follow pointer chain if specified (intermediate dereferences)
        return FollowPointerChain(baseAddr, config.PointerChain);
    }

    /// <summary>
    /// RIP-relative addressing: target = matchAddr + instructionLength + displacement
    /// Common for x64 global variable access: mov rax, [rip+disp32]
    /// </summary>
    private nint ResolveRipRelative(nint matchAddress, PatternSet config)
    {
        // Read the 32-bit displacement from the instruction
        var operandAddr = matchAddress + config.OperandOffset;
        int displacement = _memory.ReadInt32(operandAddr);

        // RIP-relative: target = instruction_end + displacement
        // instruction_end = operandAddr + 4 (displacement is always 4 bytes)
        // This works regardless of context bytes before the instruction in the pattern.
        nint instructionEnd = operandAddr + 4;
        nint target = instructionEnd + displacement;

        return target;
    }

    /// <summary>
    /// Embedded offset: the operand is a direct offset/address embedded in the instruction.
    /// </summary>
    private nint ResolveEmbeddedOffset(nint matchAddress, PatternSet config)
    {
        var operandAddr = matchAddress + config.OperandOffset;

        return config.OperandSize switch
        {
            4 => (nint)_memory.ReadInt32(operandAddr),
            8 => _memory.ReadPointer(operandAddr),
            _ => nint.Zero
        };
    }

    /// <summary>
    /// Follows a chain of pointer dereferences with offsets.
    /// chain = [offset1, offset2, ...] means:
    /// addr = ReadPointer(addr) + offset1
    /// addr = ReadPointer(addr) + offset2
    /// ...
    /// </summary>
    private nint FollowPointerChain(nint baseAddr, int[] chain)
    {
        if (chain.Length == 0) return baseAddr;

        var addr = baseAddr;
        foreach (var offset in chain)
        {
            addr = _memory.ReadPointer(addr);
            if (addr == nint.Zero) return nint.Zero;
            addr += offset;
        }

        return addr;
    }
}
