# AvailabilityView

Stránka jen pro čtení, která návštěvníkům webu merchanta (majitele účtu v Reservantu) ukazuje jeho volnou kapacitu jako kalendář nebo agendu. Vkládá se přes iframe nebo se otevírá odkazem. Hostuje ji Reservanto na IIS, případně merchant sám.

## Jak spolu pracujeme

@docs/ai-working-agreement.md
@docs/project-profile.md

## Hranice projektu

- Jedna instance = jeden merchant.
- Žádná databáze, žádné cookies, žádné uživatelské účty.
- Stránka nic v Reservantu nemění a žádá jen práva ke čtení, která vyjmenovává `docs/SPEC.md`. Rezervace (`Event_r`) se čtou jen kvůli obsazeným časům a údaje zákazníků se zahodí při načtení. Práva k zákazníkům a k zápisu do projektu nepatří.

## Dokumentace

| Soubor | Kdy číst |
|---|---|
| `docs/SPEC.md` | co stavíme |
| `docs/decisions.md` | před otázkou na uživatele: nebylo to už rozhodnuto? |
| `docs/milestones/<Mx>/` | plán, průběh a zpráva rozpracovaného milníku |
| `docs/templates/` | šablony profilu, milníku a zadání evaluace |
| `docs/conventions.md` | před psaním kódu |
| `docs/vzhled.md` | veřejné CSS rozhraní, `embed.js` a zpráva o výšce |
| `docs/instalace-iis.md` | instalace a aktualizace na IIS |
| `tests/api-recordings/README.md` | nahrávky API a jejich obnova |
| https://api.reservanto.cz | dokumentace API, popis v `/OpenApi/with-merchant.yaml?simple=True` |

## Na co si dát pozor

- **Token v adrese.** Reservanto vrací LongTimeToken jako parametr `/Auth/Callback`. ASP.NET Core loguje adresy požadavků včetně parametrů a IIS také. Bez ošetření token skončí v logu.
- **Za běhu stačí LongTimeToken.** ClientId je potřeba jen k připojení účtu. Krátkodobý token platí 30 minut a nesmí opustit server.
- **`GetLongTimeToken` není v OpenAPI.** Je to stránka pro prohlížeč, ne endpoint. Postup je v `src/Reservanto.ApiClient/Authorization/MerchantConnect.cs`.
- **Vývoj i testy běží proti produkčnímu API.** Účet merchanta je skutečný, proto se z testů volá jen čtení. Nové pole z API se posuzuje proti produkčnímu OpenAPI.
- **Rezervace nesou údaje zákazníků.** Jméno, poznámky, cena a vlastní pole nesmí do logu, textu chyb, cache, nahrávek ani na stránku. Bere se jen začátek, konec, zdroj a stav.
- **API vrací i neveřejné objekty.** Veřejnost se musí hlídat u každého čtení, zdroje mají navíc e-mail a telefon.
- **Každý typ zaměření se čte jinak.** Přehled je v sekci 4 v `docs/SPEC.md`. Dostupnost individuálních zaměření závisí na službě.
- **Časové pásmo API nevrací,** časy jsou Unix timestamp. Převádí se podle `AvailabilityView:TimeZone`, pozor na přechod na letní čas.
- **Stránkované seznamy vracejí nejvýš 100 položek.**
- **Repozitář je veřejný.** Platí sekce Veřejný repozitář v dohodě o spolupráci. Do kódu, testů, nahrávek a dokumentů nepatří tajné, soukromé ani interní údaje. ClientId a token patří do user-secrets, soukromé poznámky do `.private/`.
- **Veřejné rozhraní pro merchanty:** parametry URL, klíče konfigurace, CSS proměnné a třídy. Změna rozbije kopie u merchantů.

## Příkazy

| Příkaz | Co dělá |
|---|---|
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/check.ps1 -Mode Full` | rychlé kontroly, testy v prohlížeči, zkušební publikace a detektor duplicit (jen varování) |
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/check.ps1 -Mode Fast` | překlad a testy bez sítě a bez prohlížeče, spouští je i `Stop` hook, nastavené v `.claude/checks.json` |
| `powershell -NoProfile -ExecutionPolicy Bypass -File eng/check.ps1 -Mode Smoke` | testy proti produkčnímu API, jen čtení, token z user-secrets. Při uzavření milníku a při obnově nahrávek (`tests/api-recordings/README.md`) |
| `dotnet run --project src/AvailabilityView.Web --launch-profile AvailabilityView.Web` | spustí aplikaci na `https://localhost:5001` |

Past: `dotnet test` spouštěj bez `--nologo`, jinak skončí „spustila se nula testů“ (exit 5).

`Stop` hook pustí ukončení tahu, když je v `docs/milestones/<Mx>/progress.md` řádek `BLOCKED: <otázka>`.
