# AvailabilityView

[English](README.en.md)

Stránka jen pro čtení, která návštěvníkům webu ukazuje volnou kapacitu z účtu v [Reservantu](https://www.reservanto.cz): volné lekce a volný čas zdrojů individuálních služeb. Vkládá se do webu přes iframe nebo se otevírá odkazem. Stránka nic nerezervuje, nemá databázi, uživatelské účty ani cookies.

- **Zobrazení:** agenda, týden ve sloupcích, týden s hodinovou osou, měsíc a věta s nejbližším volným termínem.
- **Filtry:** zaměření, provozovna, služba a zdroj, pevně v odkazu nebo k přepnutí návštěvníkem.
- **Jazyky:** čeština a angličtina.
- **Vzhled:** vlastní CSS přes CSS proměnné a třídy (`docs/vzhled.md`).
- **Generátor** sestaví odkaz a kód iframe, iframe si výšku přizpůsobí obsahu.

Jedna instance slouží jednomu účtu (merchantovi). Co přesně stránka dělá, popisuje [specifikace](docs/SPEC.md).


[![Ukázka aplikace](https://img.youtube.com/vi/krZdvYJfos4/maxresdefault.jpg)](https://www.youtube.com/watch?v=krZdvYJfos4)

## Instalace na server

Návod pro IIS je v [docs/instalace-iis.md](docs/instalace-iis.md): publikace, HTTPS, připojení účtu, povolené domény pro iframe a aktualizace.

## Vývoj

### Co je potřeba

- Windows, .NET SDK 10 (verze v `global.json`).
- Node.js, jen pro skript kontrol `eng/check.ps1` (test hooku a detektor duplicit). SCSS a TypeScript překládá `dotnet build`, Node k tomu není potřeba.
- Pro testy v prohlížeči PowerShell 7 (`pwsh`) na jednorázovou instalaci Chromia.
- Pro připojení k API účet v Reservantu a vlastní ClientId. O ClientId se žádá e-mailem na obchod@reservanto.cz, s návratovou adresou `https://localhost:5001/Auth/Callback`.

### Spuštění

```powershell
dotnet dev-certs https --trust
dotnet run --project src/AvailabilityView.Web --launch-profile AvailabilityView.Web
```

Aplikace běží na `https://localhost:5001`. Nepřipojená instance odkáže na průvodce `/Setup`. Ten vás provede souhlasem v Reservantu a na konci ukáže příkaz, který uloží token do user-secrets:

```powershell
dotnet user-secrets set "Reservanto:LongTimeToken" "<token>" --project src/AvailabilityView.Web
```

Token dává přístup k účtu. Patří jen do user-secrets nebo do `appsettings.Production.json` na serveru, nikdy do repozitáře. Klíče konfigurace popisuje [specifikace v sekci 5](docs/SPEC.md#5-nastavení).

### Kontroly a testy

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File eng/check.ps1 -Mode Fast
```

| Režim | Co dělá |
|---|---|
| `Fast` | překlad s varováním jako chybou a testy bez sítě a bez prohlížeče. Testy čtou nahrané odpovědi API z `tests/api-recordings`. |
| `Full` | navíc testy v prohlížeči (Playwright, axe-core), zkušební publikace a detektor duplicit |
| `Smoke` | testy proti produkčnímu API, jen čtení. Token bere z user-secrets aplikace. |

Smoke testy jsou napsané pro testovací účet projektu. Počítají s víc než pěti zaměřeními, s lekcemi i individuálními zaměřeními a volnými termíny, s plně obsazenou lekcí a s právem `Event_r` (v průvodci zapnutá volba obsazenosti). Nad jiným účtem část z nich selže, testy bez sítě a v prohlížeči na účtu nezávisí.

Před prvním během `Full` nainstalujte Chromium (po prvním překladu testů):

```powershell
pwsh tests/AvailabilityView.Web.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Past: `dotnet test` spouštějte bez `--nologo`, jinak skončí hláškou, že se nespustil žádný test.

### Struktura

| Složka | Co v ní je |
|---|---|
| `src/AvailabilityView.Web` | stránka návštěvníka, průvodce připojením, generátor |
| `src/AvailabilityView.Application` | výpočet volna a období, bez závislosti na webu a API |
| `src/AvailabilityView.Infrastructure` | čtení dat z API po typech zaměření, cache |
| `src/Reservanto.ApiClient` | klient Reservanto API, jen čtení |
| `tests/` | testy ke každému projektu, architektonická pravidla, kontroly repozitáře, testy v prohlížeči, nahrávky API |
| `docs/` | specifikace, rozhodnutí, konvence, vzhled, návod pro IIS |

## Přispívání

Chyby a návrhy hlaste přes issues, změny posílejte jako pull request.

- Kód píšte podle [konvencí](docs/conventions.md). Komentáře jsou česky a vysvětlují proč.
- Před pull requestem musí projít `eng/check.ps1 -Mode Fast`. Změna stránky by měla projít i `-Mode Full`.
- Parametry URL, klíče konfigurace, CSS proměnné a třídy jsou veřejné rozhraní, na kterém stojí weby merchantů. Jejich změnu nejdřív popište v issue.
- Repozitář je veřejný. Do kódu, testů, nahrávek a dokumentů nepatří tokeny, ClientId, osobní údaje ani cesty z vašeho disku. Část z toho hlídají testy v `tests/AvailabilityView.Repository.Tests`.
- Nové nahrávky API vznikají nahrávačem, který odstraní tokeny, kontakty a skutečné názvy (`tests/api-recordings/README.md`).

## Vývoj s AI

Projekt se vyvíjí s [Claude Code](https://claude.com/claude-code). V repozitáři je jeho nastavení:

- `CLAUDE.md` a `.claude/`: pokyny pro AI, `Stop` hook, který před koncem tahu spustí rychlé kontroly, a povolené příkazy.
- `docs/ai-working-agreement.md`: jak spolu člověk a AI pracují, kdo co rozhoduje a jak se práce ověřuje.
- `docs/project-profile.md`: úroveň kontrol pro jednotlivé oblasti projektu.
- `docs/templates/` a `docs/milestones/`: šablony a záznamy milníků.

Bez Claude Code tyto soubory můžete ignorovat, na překlad ani běh aplikace vliv nemají. Jako nezávislý oponent při evaluaci se volitelně používá Codex CLI.

## Licence

[MIT](LICENSE), Copyright (c) 2026 Reservanto s.r.o.
