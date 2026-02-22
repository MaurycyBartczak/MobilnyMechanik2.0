using System.Diagnostics;
using System.Text.Json;
using ETS2TruckRepair.Config;
using ETS2TruckRepair.Memory;
using ETS2TruckRepair.Repair;
using ETS2TruckRepair.Scanning;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "ETS2 Truck Repair v2.0";

Console.WriteLine("========================================");
Console.WriteLine("   ETS2 Truck & Trailer Repair v2.0");
Console.WriteLine("   CT-Based Injection Engine");
Console.WriteLine("========================================");
Console.WriteLine();

// Check for admin rights
if (!IsRunningAsAdmin())
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  UWAGA: Zalecane uruchomienie jako Administrator!");
    Console.WriteLine("  (PPM na .exe -> Uruchom jako administrator)");
    Console.ResetColor();
    Console.WriteLine();
}

// Parse CLI arguments
bool watchMode = false;
int watchInterval = 5;

foreach (var arg in args)
{
    if (arg.Equals("--watch", StringComparison.OrdinalIgnoreCase))
        watchMode = true;
    else if (arg.StartsWith("--interval=", StringComparison.OrdinalIgnoreCase))
    {
        if (int.TryParse(arg["--interval=".Length..], out var iv) && iv > 0)
            watchInterval = iv;
    }
    else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg == "-h")
    {
        PrintHelp();
        return;
    }
}

if (watchMode)
    await RunWatchMode(watchInterval);
else
    RunOnceMode();

// === Implementation ===

static void RunOnceMode()
{
    Console.WriteLine("Szukam procesu eurotrucks2.exe...");
    Console.WriteLine();

    using var engine = new RepairEngine();
    if (!engine.AttachToGame())
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  Nie znaleziono procesu eurotrucks2.exe!");
        Console.WriteLine("  Uruchom gre i sprobuj ponownie.");
        Console.ResetColor();
        WaitForKey();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("\n  UWAGA: Musisz JECHAC (nie stac w menu/serwisie)!");
    Console.WriteLine("  Funkcja wear musi byc aktywna.\n");
    Console.ResetColor();

    bool truckOk = engine.RepairTruck();
    // bool trailerOk = engine.RepairTrailer(); // wyłączone - powoduje crash

    Console.WriteLine();
    if (truckOk)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("========================================");
        Console.WriteLine("  Naprawa zakonczona!");
        Console.WriteLine("  Wear/damage zablokowany (NOP).");
        Console.WriteLine("========================================");
        Console.ResetColor();

        // Zeruj koła przez 10 sekund (5 ticków co 2s) bo koła mogą być przeliczane z opóźnieniem
        Console.WriteLine("\n  Zerowanie kol (10s)...");
        for (int tick = 0; tick < 5; tick++)
        {
            Thread.Sleep(2000);
            int fixed2 = engine.RepairWheelsTick();
            if (fixed2 > 0)
                Console.WriteLine($"    Tick {tick + 1}: zerowano {fixed2} wartosci.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n  Gotowe! Mozesz zamknac program.");
        Console.WriteLine("  NOPy zostaja aktywne do zamkniecia gry.");
        Console.ResetColor();
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("========================================");
        Console.WriteLine("  Nie udalo sie naprawic.");
        Console.WriteLine("  Sprawdz czy jedziesz (nie stoisz w menu).");
        Console.WriteLine("========================================");
        Console.ResetColor();
        WaitForKey();
    }
}

static async Task RunWatchMode(int intervalSeconds)
{
    Console.WriteLine($"Watch mode aktywny (co {intervalSeconds}s). Ctrl+C aby zakonczyc.");
    Console.WriteLine("========================================");
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (!cts.Token.IsCancellationRequested)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        Console.WriteLine($"--- [{timestamp}] ---");

        using var engine = new RepairEngine();
        if (engine.AttachToGame())
        {
            engine.RepairTruck();
            engine.RepairTrailer();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Gra nie znaleziona, czekam...");
            Console.ResetColor();
        }

        Console.WriteLine();

        try
        {
            await Task.Delay(intervalSeconds * 1000, cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("Watch mode zakonczony.");
}

static void PrintHelp()
{
    Console.WriteLine("Uzycie: ETS2TruckRepair [opcje]");
    Console.WriteLine();
    Console.WriteLine("  Po prostu uruchom podczas jazdy - program sam znajdzie");
    Console.WriteLine("  i naprawi uszkodzenia ciezarowki i naczepy.");
    Console.WriteLine();
    Console.WriteLine("Opcje:");
    Console.WriteLine("  --watch          Tryb ciaglego monitorowania");
    Console.WriteLine("  --interval=N     Interwal watcha w sekundach (domyslnie: 5)");
    Console.WriteLine("  --help, -h       Wyswietl te pomoc");
    Console.WriteLine();
    Console.WriteLine("Przyklady:");
    Console.WriteLine("  ETS2TruckRepair.exe                     Jednorazowa naprawa");
    Console.WriteLine("  ETS2TruckRepair.exe --watch              Ciagle monitorowanie");
    Console.WriteLine("  ETS2TruckRepair.exe --watch --interval=3");
}

static void WaitForKey()
{
    Console.WriteLine();
    Console.WriteLine("Nacisnij dowolny klawisz, aby zamknac...");
    Console.ReadKey(true);
}

static bool IsRunningAsAdmin()
{
    try
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch
    {
        return false;
    }
}
