# ETS2 Truck Repair Tool

Prosta aplikacja konsolowa do naprawy uszkodzeń ciężarówki w **Euro Truck Simulator 2** poprzez edycję pamięci.

## ⚠️ Uwaga

- **Nie używaj w TruckersMP** — multiplayer ma antycheat i może zbanować
- **Wymaga uruchomienia jako Administrator**
- **Offsety są placeholders** — musisz zeskanować własne dla ETS2 1.55+

## Szybki start

1. Uruchom ETS2 i wjedź ciężarówką (musisz być w grze, nie w menu)
2. Uruchom `ETS2TruckRepair.exe` jako Administrator
3. Naprawa powinna się wykonać automatycznie

## Jak zdobyć offsety

Obecne offsety w kodzie są **placeholders** i prawdopodobnie nie zadziałają.
Zobacz [POINTER_SCAN_GUIDE.md](docs/POINTER_SCAN_GUIDE.md) jak znaleźć aktualne adresy.

Po zeskanowaniu edytuj `src/ETS2TruckRepair/Repair/DamageOffsets.cs`.

## Struktura projektu

```
ETS2TruckRepair/
├── publish/                    # Skompilowana aplikacja
│   └── ETS2TruckRepair.exe
├── src/ETS2TruckRepair/
│   ├── Native/                 # P/Invoke do kernel32.dll
│   │   ├── Kernel32.cs
│   │   └── ProcessAccess.cs
│   ├── Memory/                 # Operacje na pamięci
│   │   └── ProcessMemory.cs
│   ├── Repair/                 # Logika naprawy
│   │   ├── DamageOffsets.cs    # ← EDYTUJ TE WARTOŚCI
│   │   └── TruckRepairService.cs
│   └── Program.cs
└── docs/
    └── POINTER_SCAN_GUIDE.md   # Jak znaleźć offsety
```

## Wymagania

- Windows 10/11 x64
- .NET 8.0 Runtime
- Euro Truck Simulator 2 1.55+

## Build

```powershell
cd src\ETS2TruckRepair
dotnet publish -c Release -o ..\..\publish
```

## Licencja

Do użytku osobistego. Nie rozpowszechniaj.
