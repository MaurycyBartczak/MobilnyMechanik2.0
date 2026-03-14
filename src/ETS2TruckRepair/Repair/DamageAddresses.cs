namespace ETS2TruckRepair.Repair;

/// <summary>
/// Holds resolved runtime addresses for damage values (computed per session via pattern scan).
/// </summary>
public sealed class DamageAddresses
{
    public required string Label { get; init; }
    public required nint BaseAddress { get; init; }
    public required (string Name, int Offset)[] Fields { get; init; }

    public nint GetFieldAddress(int index) => BaseAddress + Fields[index].Offset;
}
