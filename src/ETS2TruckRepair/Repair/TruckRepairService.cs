using System.Diagnostics;
using ETS2TruckRepair.Config;
using ETS2TruckRepair.Memory;
using ETS2TruckRepair.Scanning;

namespace ETS2TruckRepair.Repair;

public record RepairResult(bool Success, string Message, Dictionary<string, float>? DamageBefore = null);

public sealed class TruckRepairService : IDisposable
{
    private const string ProcessName = "eurotrucks2";

    private readonly ScanConfig _config;
    private ProcessMemory? _memory;
    private Process? _gameProcess;
    private nint _moduleBase;
    private int _moduleSize;

    // Cached resolved addresses (valid for current process lifetime)
    private DamageAddresses? _truckDamage;
    private DamageAddresses? _trailerDamage;

    public TruckRepairService(ScanConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Full repair: find process, scan patterns, repair truck + trailer.
    /// </summary>
    public RepairResult RepairAll()
    {
        try
        {
            if (!EnsureAttached())
                return new RepairResult(false, $"Nie znaleziono procesu {ProcessName}.exe. Upewnij sie, ze gra jest uruchomiona.");

            // Resolve truck damage addresses (scan or use cache)
            if (_truckDamage is null)
            {
                _truckDamage = ResolveDamageAddresses(_config.Truck, "Ciezarowka");
            }

            // Resolve trailer damage addresses
            if (_trailerDamage is null)
            {
                _trailerDamage = ResolveDamageAddresses(_config.Trailer, "Naczepa");
            }

            if (_truckDamage is null && _trailerDamage is null)
            {
                return new RepairResult(false,
                    "Nie udalo sie znalezc adresow uszkodzen.\n" +
                    "Mozliwe przyczyny:\n" +
                    "  - Patterny w patterns.json nie pasuja do tej wersji gry\n" +
                    "  - Brak fallback offsetow dla tej wersji\n" +
                    "  - Uruchom jako Administrator\n" +
                    "  - Uzyj Cheat Engine aby znalezc nowe patterny (patrz docs/)");
            }

            var allDamageBefore = new Dictionary<string, float>();
            bool anyRepaired = false;

            // Repair truck
            if (_truckDamage is not null)
            {
                var (success, before) = RepairEntity(_truckDamage);
                foreach (var kvp in before) allDamageBefore[kvp.Key] = kvp.Value;
                anyRepaired |= success;
            }

            // Repair trailer
            if (_trailerDamage is not null)
            {
                var (success, before) = RepairEntity(_trailerDamage);
                foreach (var kvp in before) allDamageBefore[kvp.Key] = kvp.Value;
                anyRepaired |= success;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  Naczepa: nie wykryto lub brak patternu");
                Console.ResetColor();
            }

            return anyRepaired
                ? new RepairResult(true, "Naprawa zakonczona!", allDamageBefore)
                : new RepairResult(false, "Nie udalo sie zapisac wartosci do pamieci.", allDamageBefore);
        }
        catch (Exception ex)
        {
            return new RepairResult(false, $"Blad: {ex.Message}");
        }
    }

    /// <summary>
    /// Repair using cached addresses (for watch mode). Re-scans if read fails.
    /// </summary>
    public RepairResult RepairCached()
    {
        if (_memory is null || _gameProcess is null || _gameProcess.HasExited)
        {
            // Process gone, clear cache and re-attach
            InvalidateCache();
            return RepairAll();
        }

        bool anyDamaged = false;
        var allBefore = new Dictionary<string, float>();

        // Check truck
        if (_truckDamage is not null)
        {
            var damage = ReadDamageValues(_truckDamage);
            if (damage.Values.Any(v => float.IsNaN(v) || v < 0))
            {
                // Read failed, cache stale
                InvalidateCache();
                return RepairAll();
            }

            foreach (var kvp in damage) allBefore[kvp.Key] = kvp.Value;

            if (damage.Values.Any(v => v > _config.Watch.RepairThreshold))
            {
                anyDamaged = true;
                WriteDamageValues(_truckDamage, 0.0f);
            }
        }

        // Check trailer
        if (_trailerDamage is not null)
        {
            var damage = ReadDamageValues(_trailerDamage);
            if (damage.Values.Any(v => float.IsNaN(v) || v < 0))
            {
                _trailerDamage = null; // trailer might have changed
                _trailerDamage = ResolveDamageAddresses(_config.Trailer, "Naczepa");
            }
            else
            {
                foreach (var kvp in damage) allBefore[kvp.Key] = kvp.Value;

                if (damage.Values.Any(v => v > _config.Watch.RepairThreshold))
                {
                    anyDamaged = true;
                    WriteDamageValues(_trailerDamage, 0.0f);
                }
            }
        }

        if (!anyDamaged)
            return new RepairResult(true, "Brak uszkodzen powyzej progu.", allBefore);

        return new RepairResult(true, "Naprawiono!", allBefore);
    }

    /// <summary>
    /// Attaches to game process and opens memory handle.
    /// </summary>
    public bool EnsureAttached()
    {
        if (_memory is not null && _gameProcess is not null && !_gameProcess.HasExited)
            return true;

        // Clean up previous
        _memory?.Dispose();
        _memory = null;
        _gameProcess?.Dispose();
        _gameProcess = null;

        // Use Toolhelp32 API (works under Wine/Proton)
        var (pid, exeName) = Native.Kernel32.FindProcessByName(ProcessName);
        if (pid == 0) return false;

        _gameProcess = Process.GetProcessById(pid);
        Console.WriteLine($"  Proces: {exeName} (PID: {pid})");

        var (moduleBase, moduleSize) = ModuleHelper.GetModuleInfo(_gameProcess);
        if (moduleBase == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("  Nie mozna odczytac adresu bazowego. Uruchom jako Administrator.");
            Console.ResetColor();
            return false;
        }

        _moduleBase = moduleBase;
        _moduleSize = moduleSize;
        Console.WriteLine($"  Modul bazowy: 0x{moduleBase:X} (rozmiar: {moduleSize / 1024}KB)");

        _memory = new ProcessMemory(_gameProcess.Id, moduleBase);
        return _memory.IsValid;
    }

    private DamageAddresses? ResolveDamageAddresses(PatternSet patternConfig, string label)
    {
        if (_memory is null) return null;

        // Try pattern scan first
        if (!string.IsNullOrWhiteSpace(patternConfig.Pattern))
        {
            Console.WriteLine($"\n  [{label}] Skanowanie patternu...");
            var result = TryPatternScan(patternConfig, label);
            if (result is not null) return result;
        }

        // Try fallback offsets
        var fallbackResult = TryFallback(patternConfig, label);
        if (fallbackResult is not null) return fallbackResult;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [{label}] Nie znaleziono adresow uszkodzen.");
        Console.ResetColor();
        return null;
    }

    private DamageAddresses? TryPatternScan(PatternSet config, string label)
    {
        if (_memory is null) return null;

        var (patternBytes, mask) = PatternScanner.ParsePattern(config.Pattern);
        var scanner = new PatternScanner(_memory.ProcessHandle);

        var matches = scanner.ScanModule(_moduleBase, _moduleSize, patternBytes, mask);

        if (matches.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{label}] Pattern nie znaleziony w pamieci.");
            Console.ResetColor();
            return null;
        }

        Console.WriteLine($"  [{label}] Znaleziono {matches.Count} dopasowanie(a).");

        int matchIdx = Math.Min(config.MatchIndex, matches.Count - 1);
        var matchAddr = matches[matchIdx];

        Console.WriteLine($"  [{label}] Dopasowanie #{matchIdx}: 0x{matchAddr:X}");

        var resolver = new AddressResolver(_memory);
        var damageBase = resolver.Resolve(matchAddr, config);

        if (damageBase == nint.Zero)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{label}] Resolwer zwrocil adres zerowy.");
            Console.ResetColor();
            return null;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [{label}] Adres bazowy damage: 0x{damageBase:X}");
        Console.ResetColor();

        // Validate: read first field, should be a float 0.0 - 1.0
        var testValue = _memory.ReadFloat(damageBase + config.DamageFields[0].Offset);
        if (float.IsNaN(testValue) || testValue < -0.01f || testValue > 1.5f)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{label}] Wartosc walidacyjna: {testValue} (poza zakresem 0.0-1.0 - mozliwy bled)");
            Console.ResetColor();
            // Don't return null - might still work, let user see the values
        }

        return new DamageAddresses
        {
            Label = label,
            BaseAddress = damageBase,
            Fields = config.DamageFields.Select(f => (f.Name, f.Offset)).ToArray()
        };
    }

    private DamageAddresses? TryFallback(PatternSet patternConfig, string label)
    {
        if (_memory is null || _gameProcess is null) return null;

        var version = ModuleHelper.GetFileVersion(_gameProcess);
        if (version is null) return null;

        // Try exact version match, then major.minor match
        FallbackEntry? entry = null;
        if (_config.Fallback.VersionOffsets.TryGetValue(version, out entry))
        {
            // exact match
        }
        else
        {
            // Try major.minor (e.g. "1.55" from "1.55.1234")
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                var shortVer = $"{parts[0]}.{parts[1]}";
                _config.Fallback.VersionOffsets.TryGetValue(shortVer, out entry);
            }
        }

        if (entry is null) return null;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  [{label}] Uzywam fallback offsetow dla wersji {version}");
        Console.ResetColor();

        nint damageBase;
        if (entry.PointerChain.Length > 0)
        {
            var fullChain = new int[entry.PointerChain.Length + 1];
            fullChain[0] = entry.BaseOffset;
            Array.Copy(entry.PointerChain, 0, fullChain, 1, entry.PointerChain.Length);
            damageBase = _memory.ResolvePointerChain(fullChain);
        }
        else
        {
            damageBase = _moduleBase + entry.BaseOffset;
        }

        if (damageBase == nint.Zero) return null;

        var fields = entry.DamageFields.Length > 0
            ? entry.DamageFields.Select(f => (f.Name, f.Offset)).ToArray()
            : patternConfig.DamageFields.Select(f => (f.Name, f.Offset)).ToArray();

        // Validate
        var testValue = _memory.ReadFloat(damageBase + fields[0].Offset);
        if (float.IsNaN(testValue) || testValue < -0.01f || testValue > 1.5f)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [{label}] Fallback walidacja nieudana: {testValue} (poza zakresem)");
            Console.ResetColor();
            return null;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [{label}] Fallback adres: 0x{damageBase:X}");
        Console.ResetColor();

        return new DamageAddresses
        {
            Label = label,
            BaseAddress = damageBase,
            Fields = fields
        };
    }

    private (bool Success, Dictionary<string, float> Before) RepairEntity(DamageAddresses addresses)
    {
        var before = ReadDamageValues(addresses);

        Console.WriteLine($"\n  {addresses.Label} - aktualne uszkodzenia:");
        foreach (var kvp in before)
        {
            var pct = kvp.Value * 100;
            var color = pct > 50 ? ConsoleColor.Red : pct > 10 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.ForegroundColor = color;
            Console.WriteLine($"    {kvp.Key}: {pct:F1}%");
            Console.ResetColor();
        }

        bool hasDamage = before.Values.Any(v => v > 0.001f);
        if (!hasDamage)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  {addresses.Label}: brak uszkodzen.");
            Console.ResetColor();
            return (true, before);
        }

        Console.WriteLine($"  {addresses.Label}: naprawiam...");
        bool writeOk = WriteDamageValues(addresses, 0.0f);

        if (!writeOk)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  {addresses.Label}: blad zapisu!");
            Console.ResetColor();
            return (false, before);
        }

        // Verify
        var after = ReadDamageValues(addresses);
        bool allZero = after.Values.All(v => v <= 0.001f);

        if (allZero)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  {addresses.Label}: naprawiono!");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  {addresses.Label}: naprawa czesciowa.");
            Console.ResetColor();
        }

        return (writeOk, before);
    }

    private Dictionary<string, float> ReadDamageValues(DamageAddresses addresses)
    {
        var result = new Dictionary<string, float>();
        if (_memory is null) return result;

        foreach (var (name, offset) in addresses.Fields)
        {
            var value = _memory.ReadFloat(addresses.BaseAddress + offset);
            result[name] = float.IsNaN(value) ? -1f : value;
        }

        return result;
    }

    private bool WriteDamageValues(DamageAddresses addresses, float value)
    {
        if (_memory is null) return false;

        bool allOk = true;
        foreach (var (_, offset) in addresses.Fields)
        {
            if (!_memory.WriteFloat(addresses.BaseAddress + offset, value))
                allOk = false;
        }

        return allOk;
    }

    private void InvalidateCache()
    {
        _truckDamage = null;
        _trailerDamage = null;
        _memory?.Dispose();
        _memory = null;
        _gameProcess?.Dispose();
        _gameProcess = null;
    }

    public void Dispose()
    {
        _memory?.Dispose();
        _gameProcess?.Dispose();
    }
}
