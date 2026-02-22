using System.Diagnostics;

namespace ETS2TruckRepair.Scanning;

public static class ModuleHelper
{
    /// <summary>
    /// Returns (baseAddress, moduleSize) for the main module or a named module.
    /// </summary>
    public static (nint Base, int Size) GetModuleInfo(Process process, string moduleName = "")
    {
        try
        {
            process.Refresh();

            if (string.IsNullOrEmpty(moduleName))
            {
                var main = process.MainModule;
                if (main is null) return (nint.Zero, 0);
                return (main.BaseAddress, main.ModuleMemorySize);
            }

            foreach (ProcessModule module in process.Modules)
            {
                if (module.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                    return (module.BaseAddress, module.ModuleMemorySize);
            }
        }
        catch
        {
            // Access denied or process exited
        }

        return (nint.Zero, 0);
    }

    /// <summary>
    /// Gets the file version of the game executable (e.g. "1.55.1234").
    /// </summary>
    public static string? GetFileVersion(Process process)
    {
        try
        {
            var path = process.MainModule?.FileName;
            if (path is null) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileVersion;
        }
        catch
        {
            return null;
        }
    }
}
