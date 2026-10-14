# Pokyny pro práci na projektu

## Projekt

Objednávky restaurace jsou mobilní webová aplikace v češtině pro obsluhu,
kuchyň a správce. Backend používá ASP.NET Core 10 a Entity Framework Core 10;
frontend React, TypeScript a Vite. Každý stůl má nejvýše jednu aktivní objednávku.
Jednotlivé položky mají samostatný stav doručení a zaplacení. Aplikace uchovává
historii oprav a audit a uzavírá objednávku po doručení a zaplacení všech položek.

## Struktura a ověřování

- `backend/RestaurantOrders.Api`: API, přihlášení, katalog, objednávky,
  reporty, databázové modely a migrace pro oba poskytovatele.
- `backend/RestaurantOrders.SmokeTests`: spustitelné integrační testy skutečného
  API; spuštění `dotnet run --project backend/RestaurantOrders.SmokeTests`.
- `frontend`: React aplikace a testy; `npm --prefix frontend test` a
  `npm --prefix frontend run test:e2e`.
- `scripts/start-local.ps1`: lokální spuštění API a frontendu s migracemi.
- `scripts/build.ps1`: sestavení distribučního ZIP a standardně kopie přes SCP
  do `waritko@mrazitko.varak.net:/home/waritko/objednavky-run`;
  `-SkipScp` vytvoří pouze lokální balíček.
  SCP vynechává soubory `appsettings*` v kořeni publikace, aby zachovalo konfiguraci
  serveru; lokální publikační adresář a ZIP je nadále obsahují.
- `docs`: dokumentace provozu, API, importů a ověřování.
- `README.md`, `PLAN.md`, `PROGRESS.md`, `spec.md`: úvod, plán, průběh a zadání.

Výchozí databází je MS SQL Server na `mrazitko.varak.net`, databáze
`RestaurantOrders`, SQL uživatel `monami`. Připojení používá šifrování a ověřování
certifikátu. Heslo dodává `Database__Password` nebo úplný řetězec v
`ConnectionStrings__RestaurantOrders`; hesla neukládej do repozitáře.
`Database__Password` má přednost před heslem v řetězci.
Vývojové prostředí (`Development`, včetně `scripts/start-local.ps1`) používá
na stejném serveru databázi `klient_monami` a uživatele `monami`.
Vývojové heslo je na tomto počítači uloženo mimo repozitář v .NET user secrets
API (`UserSecretsId=RestaurantOrders.Api-local-development`, klíč
`Database:Password`); proměnné prostředí mají přednost. Produkční výchozí
databáze zůstává `RestaurantOrders`. Integrační testy nadále používají izolované
databáze, nikoli `klient_monami`.
Ověření této vývojové konfigurace: backend se sestavil bez chyb a varování;
kontrola konfigurace a načtení lokálního hesla prošly. Pokus o připojení bez
migrací selhal na nedůvěryhodné certifikační autoritě TLS serveru na tomto
počítači. Na výslovný pokyn uživatele vývojová konfigurace nyní používá
`Encrypt=True;TrustServerCertificate=True`; produkční výchozí konfigurace
nadále ověřuje certifikát. Migrace ani integrační testy na `klient_monami`
nebyly spuštěny.
Po zapnutí důvěry certifikátu prošlo připojení pomocí lokálního hesla a kontrola
`SELECT DB_NAME()` vrátila `klient_monami`; ověření neprovádělo zápisy ani migrace.
SQLite je volitelná alternativa přes `Database__Provider=Sqlite` a odpovídající
připojovací řetězec.
Start API automaticky aplikuje migrace; chybějící heslo SQL autentizace skončí
chybou konfigurace ještě před migracemi. Testy používají izolované databáze,
nikdy neprováděj integrační testy proti provozní databázi.

Ověření změny výchozí databáze: sestavení backendu a kompletní integrační sada
SQLite prošly, stejně jako kontrola formátování změněného `Program.cs` a kontrola
odmítnutí SQL startu bez hesla. Připojení k vzdálenému SQL Serveru vyžaduje dodané
heslo a dosud nebylo ověřeno. Celková kontrola `dotnet format` hlásí existující
problémy s kódováním v obou migracích `20261006151612_OrderNotes.cs` (SQLite)
a `20261006151712_OrderNotes.cs` (SQL Server).

Všechny entity a jejich odkazy používají databází generované klíče `int`
(SQL Server `IDENTITY`, SQLite `AUTOINCREMENT`); API vrací ID jako JSON čísla.
GUID zůstává pouze pro `ConcurrencyToken` a samostatný `OrderUnit.RequestToken`
s unikátním indexem. Přidání položky přijímá `{menuItemId, requestToken}`; klient
generuje UUID pro každý klik a zachovává jej při opakování požadavku.
Migrace `IntegerIds` pro oba poskytovatele transakčně přestaví osm tabulek
a převede původní ID, odkazy, klíče poznámek a ID položek v auditu. Původní
GUID jednotky se zachová jako retry token. Před nasazením zastav všechny
instance a vytvoř zálohu; návrat vyžaduje obnovu zálohy, `Down` je odmítnut.
Staré klienty je nutné obnovit a znovu přihlásit; čekající kliky se starými
ID nelze přehrát. Podrobnosti jsou v `docs/deployment.md` a `docs/orders.md`.
Integrační sada obsahuje `MigrationChecks`: upgrade naplněné staré databáze
s poznámkami i bez nich, kontrolu vztahů, historie, auditu a generování ID.
SQL Server ověřuj pouze s `TEST_SQLSERVER_CONNECTION` na izolované instanci
příkazem `dotnet run --project backend/RestaurantOrders.SmokeTests -- --sqlserver`.
Ověření převodu ID: kompletní integrační sada SQLite, šest frontendových testů,
produkční sestavení a oba mobilní E2E scénáře prošly. Kontrola formátování
změněných souborů a kontrola SQL Server modelu proti snapshotu prošly; SQL
migrační skript byl vygenerován. Spuštění migrace na SQL Serveru zůstává
neověřené bez izolovaného testovacího připojení. Mazání dočasných SQLite
databází v testech krátce opakuje pokus při dočasném zámku souboru ve Windows.

## Pravidla

- Po každém vyřešeném požadavku vytvoř git commit s relevantními změnami.
  Do commitu nezahrnuj nesouvisející změny uživatele ani tajné údaje.
- Relevantní změny projektu ukládej průběžně do tohoto `AGENTS.md`, zejména
  změny architektury, konfigurace, pracovních postupů a ověřovacích příkazů.
- Před commitem proveď ověření odpovídající změně a ve výsledku popiš případná
  omezení ověření.
