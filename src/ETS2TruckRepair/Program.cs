using ETS2TruckRepair.Repair;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "MobilnyMechanik";

Console.WriteLine("========================================");
Console.WriteLine("       MobilnyMechanik v2.0");
Console.WriteLine("   ETS2 Truck Repair Tool");
Console.WriteLine("========================================");
Console.WriteLine();

// Parse CLI arguments
int intervalSeconds = 3;

foreach (var arg in args)
{
    if (arg.StartsWith("--interval=", StringComparison.OrdinalIgnoreCase))
    {
        if (int.TryParse(arg["--interval=".Length..], out var iv) && iv > 0)
            intervalSeconds = iv;
    }
    else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg == "-h")
    {
        PrintHelp();
        return;
    }
}

RunRepairLoop(intervalSeconds);

// === Implementation ===

static void RunRepairLoop(int intervalSeconds)
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

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  Program dziala w tle. Ctrl+C aby zakonczyc.");
    Console.WriteLine($"  Naprawa co {intervalSeconds}s (--interval=N zmienia).");
    Console.ResetColor();
    Console.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    bool repaired = false;

    while (!cts.Token.IsCancellationRequested)
    {
        if (!repaired)
        {
            // Pelna naprawa — capture + zerowanie + RET patch
            bool ok = engine.RepairTruck();
            if (ok)
            {
                repaired = true;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n  Naprawa zakonczona! Wear zablokowany (RET).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n  Nie udalo sie — ponowna proba za {intervalSeconds}s...");
                Console.ResetColor();
            }
        }
        else
        {
            // Juz naprawione — zeruj wartosci ktore mogly wrocic po kolizji
            engine.RepairWheelsTick();
        }

        // Czekaj interval
        for (int i = 0; i < intervalSeconds && !cts.Token.IsCancellationRequested; i++)
            Thread.Sleep(1000);
    }

    Console.WriteLine("\n  Zakonczono. RET patch zostaje aktywny do zamkniecia gry.");
}

static void PrintHelp()
{
    Console.WriteLine("Uzycie: MobilnyMechanik.exe [opcje]");
    Console.WriteLine();
    Console.WriteLine("  Uruchom podczas jazdy. Program naprawi ciezarowke,");
    Console.WriteLine("  zablokuje wear i bedzie co minute zerowac uszkodzenia");
    Console.WriteLine("  ktore moga wrocic po kolizji.");
    Console.WriteLine();
    Console.WriteLine("Opcje:");
    Console.WriteLine("  --interval=N     Interwal zerowania w sekundach (domyslnie: 60)");
    Console.WriteLine("  --help, -h       Wyswietl te pomoc");
    Console.WriteLine();
    Console.WriteLine("Przyklady:");
    Console.WriteLine("  MobilnyMechanik.exe                  Naprawa + petla co 60s");
    Console.WriteLine("  MobilnyMechanik.exe --interval=30    Naprawa + petla co 30s");
}

static void WaitForKey()
{
    Console.WriteLine();
    Console.WriteLine("Nacisnij dowolny klawisz, aby zamknac...");
    Console.ReadKey(true);
}
