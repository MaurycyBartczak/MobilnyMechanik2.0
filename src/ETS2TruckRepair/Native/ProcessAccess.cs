namespace ETS2TruckRepair.Native;

/// <summary>
/// Process access rights for OpenProcess
/// </summary>
[Flags]
public enum ProcessAccessFlags : uint
{
    /// <summary>
    /// Required to read memory
    /// </summary>
    VmRead = 0x0010,

    /// <summary>
    /// Required to write memory
    /// </summary>
    VmWrite = 0x0020,

    /// <summary>
    /// Required for VirtualProtectEx
    /// </summary>
    VmOperation = 0x0008,

    /// <summary>
    /// Required to query process info
    /// </summary>
    QueryInformation = 0x0400,

    /// <summary>
    /// All access rights for memory editing
    /// </summary>
    AllForMemory = VmRead | VmWrite | VmOperation | QueryInformation
}
