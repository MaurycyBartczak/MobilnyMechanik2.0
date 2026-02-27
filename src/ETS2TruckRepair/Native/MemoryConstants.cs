namespace ETS2TruckRepair.Native;

public static class MemoryConstants
{
    public const uint MemCommit = 0x1000;
    public const uint PageExecute = 0x10;
    public const uint PageExecuteRead = 0x20;
    public const uint PageExecuteReadWrite = 0x40;
    public const uint PageExecuteWriteCopy = 0x80;
    public const uint PageReadOnly = 0x02;
    public const uint PageReadWrite = 0x04;
}
