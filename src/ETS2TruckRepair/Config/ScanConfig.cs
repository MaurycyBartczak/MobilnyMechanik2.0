namespace ETS2TruckRepair.Config;

public sealed class ScanConfig
{
    public PatternSet Truck { get; set; } = new();
    public PatternSet Trailer { get; set; } = new();
    public WatchSettings Watch { get; set; } = new();
    public FallbackSettings Fallback { get; set; } = new();
}

public sealed class PatternSet
{
    public string Description { get; set; } = "";

    /// <summary>
    /// AOB pattern with ?? wildcards, e.g. "F3 0F 10 81 ?? ?? ?? ??"
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// Module to scan. Empty = main module (eurotrucks2.exe).
    /// </summary>
    public string Module { get; set; } = "";

    /// <summary>
    /// How to extract the target address from matched instruction.
    /// "rip_relative" - RIP-relative addressing (common in x64 code)
    /// "embedded_offset" - raw displacement at OperandOffset
    /// "pointer_chain" - follow chain from extracted base
    /// </summary>
    public string ExtractionMethod { get; set; } = "rip_relative";

    /// <summary>
    /// Byte offset within matched pattern where the operand is located.
    /// </summary>
    public int OperandOffset { get; set; }

    /// <summary>
    /// Size of the operand in bytes (typically 4 for 32-bit displacement).
    /// </summary>
    public int OperandSize { get; set; } = 4;

    /// <summary>
    /// Total instruction length for RIP-relative calculation.
    /// target = matchAddr + InstructionLength + displacement
    /// </summary>
    public int InstructionLength { get; set; }

    /// <summary>
    /// After resolving base pointer, apply this chain of dereferences+offsets.
    /// </summary>
    public int[] PointerChain { get; set; } = [];

    /// <summary>
    /// Offsets from the resolved damage struct base to individual float fields.
    /// </summary>
    public DamageFieldConfig[] DamageFields { get; set; } = [];

    /// <summary>
    /// Which match to use if multiple found (0-based). Default: 0 (first).
    /// </summary>
    public int MatchIndex { get; set; }
}

public sealed class DamageFieldConfig
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
}

public sealed class WatchSettings
{
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 5;
    public float RepairThreshold { get; set; } = 0.01f;
}

public sealed class FallbackSettings
{
    /// <summary>
    /// Fallback offsets per game version (e.g. "1.55").
    /// </summary>
    public Dictionary<string, FallbackEntry> VersionOffsets { get; set; } = new();
}

public sealed class FallbackEntry
{
    public int BaseOffset { get; set; }
    public int[] PointerChain { get; set; } = [];
    public DamageFieldConfig[] DamageFields { get; set; } = [];
}
