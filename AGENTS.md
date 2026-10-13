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
- `docs`: dokumentace provozu, API, importů a ověřování.
- `README.md`, `PLAN.md`, `PROGRESS.md`, `spec.md`: úvod, plán, průběh a zadání.

Výchozí databází je MS SQL Server na `mrazitko.varak.net`, databáze
`RestaurantOrders`, SQL uživatel `monami`. Připojení používá šifrování a ověřování
certifikátu. Heslo dodává `Database__Password` nebo úplný řetězec v
`ConnectionStrings__RestaurantOrders`; hesla neukládej do repozitáře.
`Database__Password` má přednost před heslem v řetězci. SQLite je volitelná
alternativa přes `Database__Provider=Sqlite` a odpovídající připojovací řetězec.
Start API automaticky aplikuje migrace; chybějící heslo SQL autentizace skončí
chybou konfigurace ještě před migracemi. Testy používají izolované databáze,
nikdy neprováděj integrační testy proti provozní databázi.

Ověření změny výchozí databáze: sestavení backendu a kompletní integrační sada
SQLite prošly, stejně jako kontrola formátování změněného `Program.cs` a kontrola
odmítnutí SQL startu bez hesla. Připojení k vzdálenému SQL Serveru vyžaduje dodané
heslo a dosud nebylo ověřeno. Celková kontrola `dotnet format` hlásí existující
problémy s kódováním v obou migracích `20261006151612_OrderNotes.cs` (SQLite)
a `20261006151712_OrderNotes.cs` (SQL Server).

## Pravidla

- Po každém vyřešeném požadavku vytvoř git commit s relevantními změnami.
  Do commitu nezahrnuj nesouvisející změny uživatele ani tajné údaje.
- Relevantní změny projektu ukládej průběžně do tohoto `AGENTS.md`, zejména
  změny architektury, konfigurace, pracovních postupů a ověřovacích příkazů.
- Před commitem proveď ověření odpovídající změně a ve výsledku popiš případná
  omezení ověření.
