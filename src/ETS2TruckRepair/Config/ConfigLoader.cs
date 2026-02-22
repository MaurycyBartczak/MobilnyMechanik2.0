using System.Text.Json;

namespace ETS2TruckRepair.Config;

public static class ConfigLoader
{
    private const string DefaultFileName = "patterns.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ScanConfig Load(string? path = null)
    {
        var configPath = ResolveConfigPath(path);

        if (configPath is null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Nie znaleziono {DefaultFileName} - uzywam domyslnych ustawien.");
            Console.ResetColor();
            return CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ScanConfig>(json, JsonOptions);
            return config ?? CreateDefault();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Blad odczytu konfiguracji: {ex.Message}");
            Console.WriteLine("Uzywam domyslnych ustawien.");
            Console.ResetColor();
            return CreateDefault();
        }
    }

    private static string? ResolveConfigPath(string? explicitPath)
    {
        if (explicitPath is not null && File.Exists(explicitPath))
            return explicitPath;

        // Next to the exe
        var exeDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(exeDir, DefaultFileName);
        if (File.Exists(candidate))
            return candidate;

        // Current directory
        candidate = Path.Combine(Directory.GetCurrentDirectory(), DefaultFileName);
        if (File.Exists(candidate))
            return candidate;

        return null;
    }

    private static ScanConfig CreateDefault() => new()
    {
        Truck = new PatternSet
        {
            Description = "Truck damage - placeholder pattern (update with real AOB from CE/x64dbg)",
            Pattern = "",
            DamageFields =
            [
                new() { Name = "Silnik", Offset = 0 },
                new() { Name = "Skrzynia biegow", Offset = 4 },
                new() { Name = "Kabina", Offset = 8 },
                new() { Name = "Podwozie", Offset = 12 },
                new() { Name = "Kola", Offset = 16 }
            ]
        },
        Trailer = new PatternSet
        {
            Description = "Trailer damage - placeholder pattern",
            Pattern = "",
            DamageFields =
            [
                new() { Name = "Nadwozie", Offset = 0 },
                new() { Name = "Podwozie", Offset = 4 },
                new() { Name = "Kola", Offset = 8 },
                new() { Name = "Ladunek", Offset = 12 }
            ]
        },
        Watch = new WatchSettings
        {
            Enabled = false,
            IntervalSeconds = 5,
            RepairThreshold = 0.01f
        }
    };
}
