# ETS2 Truck Repair

Narzędzie do naprawy ciężarówki w **Euro Truck Simulator 2** poprzez modyfikację pamięci gry.

Program naprawia ciężarówkę w Euro Truck Simulator 2 poprzez bezpośrednią modyfikację pamięci gry podczas jej działania. Nie zmienia żadnych plików na dysku — wszystkie zmiany istnieją tylko w pamięci RAM i znikają po zamknięciu gry.

Po uruchomieniu program znajduje proces gry i skanuje jej kod wykonywalny szukając charakterystycznej sekwencji bajtów, tzw. AOB pattern. Ta sekwencja to unikalna sygnatura funkcji odpowiedzialnej za naliczanie zużycia pojazdu — coś jak odcisk palca, po którym można ją rozpoznać niezależnie od tego, gdzie w pamięci została załadowana.

Następnie program musi dowiedzieć się, gdzie w pamięci znajduje się aktualny pojazd gracza. Robi to przez chwilowe wstrzyknięcie kilku bajtów kodu na początek funkcji zużycia. Gdy gra normalnie wywołuje tę funkcję podczas jazdy, wstrzyknięty kod zapisuje adres pojazdu, a potem oddaje sterowanie oryginalnej funkcji. Cała operacja trwa ułamek sekundy i jest niezauważalna dla gracza.

Znając adres pojazdu, program dociera do struktury danych przechowującej stan zużycia. Gra trzyma tam wartości od 0.0 (brak uszkodzeń) do 1.0 (całkowite zniszczenie) dla każdego komponentu — silnika, kabiny, skrzyni biegów, podwozia, poszczególnych kół i opon. Program zapisuje zera we wszystkich tych polach, co natychmiast naprawia ciężarówkę.

Samo wyzerowanie wartości nie wystarczyłoby, bo gra ciągle przelicza zużycie i po kilku sekundach uszkodzenia wróciłyby do poprzedniego poziomu. Dlatego program zamienia pierwszy bajt funkcji zużycia na instrukcję natychmiastowego powrotu (RET). Od tego momentu za każdym razem, gdy gra próbuje przeliczyć zużycie, funkcja natychmiast się kończy bez wykonania żadnych obliczeń. Zużycie przestaje narastać, a ciężarówka pozostaje naprawiona. Funkcja odpowiadająca za uszkodzenia od kolizji nie jest modyfikowana, więc uderzenia nadal powodują obrażenia — gracz nie staje się niezniszczalny.

Blokada zużycia utrzymuje się w pamięci dopóki gra działa. Po zamknięciu i ponownym uruchomieniu gry cały kod wraca do normy, a zużycie znów zaczyna się naliczać. Program można wtedy uruchomić ponownie — wykryje, że funkcja jest już zablokowana, tymczasowo ją odblokuje żeby przechwycić nowy adres pojazdu, naprawi ciężarówkę i zablokuje ponownie.

## Uwagi

- **Nie używaj w TruckersMP** — multiplayer ma antycheat i może zbanować
- Wymaga uruchomienia jako **Administrator**
- Działa tylko na **Windows x64**
- AOB patterny pasują do konkretnej wersji ETS2 — po aktualizacji gry mogą wymagać zmiany

## Użycie

1. Uruchom ETS2 i wjedź ciężarówką na drogę (musisz jechać, nie stać w menu)
2. Uruchom `ETS2TruckRepair.exe` jako Administrator
3. Program naprawia ciężarówkę i blokuje zużycie
4. Można uruchamiać wielokrotnie (np. po kolizji)


Wynikiem jest jeden plik `ETS2TruckRepair.exe` (~64MB, zawiera .NET runtime).
