# Konvence kódu

Pravidla pro každého, kdo do projektu píše kód, člověka i AI. U pravidla je uvedeno, co ho hlídá. Pravidlo bez kontroly je dohoda, kterou hlídá review.

Čísla sekcí (A1, B2, …) používají hlášky kontrol v `tests/AvailabilityView.Repository.Tests/CodeRules.cs`. Při přečíslování je uprav také.

---

## A. Návrh kódu

### A1. Vrstvy a směr závislostí

Řešení dělí oddělené projekty a závislosti míří dovnitř.

```
Web ─────► Application
   └─────► Infrastructure  (jen kvůli registraci v Program.cs)

Infrastructure ──► Application  (implementuje její rozhraní)
               └─► Reservanto.ApiClient

Application          ──► nikdo
Reservanto.ApiClient ──► nic z projektu
```

- `Application` definuje rozhraní, která implementuje `Infrastructure`. Nezná typy ASP.NET Core (`HttpContext`, `IActionResult`). Web je překládá na vlastní typy aplikace.
- Rozhraní v `Application` jsou doménová, bez názvu cizího systému (`IAvailabilityReader`, ne `IReservantoReader`).
- Klient API je samostatný projekt bez závislosti na zbytku řešení. Web a Application ho nepoužívají vůbec.
- Samostatný projekt `Domain` není, doménové typy žijí v `Application`.

Hlídá: `tests/AvailabilityView.Architecture.Tests` (reference v csproj i použití typů přes ArchUnitNET, každé pravidlo má negativní test), použití klienta mimo Infrastructure `CodeRules`.

### A2. Závislosti přes DI

- Projekt má vlastní rozšiřující metodu `Add…` na `IServiceCollection` (`AddAvailabilityViewInfrastructure`, `AddReservantoApiClient`), `Program.cs` je jen skládá.
- Výchozí registrace přes `TryAdd…`, aby šly přebít pozdější registrací.
- Konfigurace, která nemůže fungovat, se ověří při startu a aplikace nenastartuje. Chyba se neodkládá na první požadavek.
- Závislosti jdou přes primární konstruktor.
- Čas jen přes `TimeProvider`. `DateTime.Now`, `UtcNow` a `Today` se nepoužívají, hlídá `CodeRules`.

### A3. Polymorfismus místo větvení

Žádné větvení podle typu zaměření ve sdíleném kódu. Osou variability tohoto projektu je typ zaměření (`docs/SPEC.md` sekce 4): každý typ má vlastní čtecí třídu `*AvailabilitySource` v `src/AvailabilityView.Infrastructure/Availability/`. Nový typ = nová třída, ne další `if` nebo `switch`. Hlídá `CodeRules`, typy z API znají jen čtecí třídy.

- Třídy jsou `sealed`. Otevřené jsou jen ty, od kterých se má dědit.
- Data bez chování jsou `record`. Implementační třídy, které nejsou kontraktem, jsou `internal`.
- Žádné zástupné třídy ani prázdné soubory dopředu. Kód vzniká, až je potřeba.

### A4. DRY

Logika se neopakuje. Co se opakuje ve dvou třídách, patří do společné třídy nebo služby. Detektor duplicit `jscpd` (`.jscpd.json`) běží v `eng/check.ps1 -Mode Full` jako varování.

### A5. Chyby

- Porušený vnitřní předpoklad = `InvalidOperationException`. Stav, který má volající rozlišit, má vlastní typ výjimky popsaný v `<exception>` u rozhraní.
- Texty výjimek česky a bez osobních údajů.
- Vstupy veřejných metod se hlídají přes `ArgumentNullException.ThrowIfNull`.
- Obecný `catch (Exception)` jen s filtrem `when`, aby neprošlo zrušení operace (`when (error is not OperationCanceledException)`).

### A6. Logování

Logování přes zdrojově generované `[LoggerMessage]` ve statické `partial` třídě `<Oblast>Log` s vlastním `EventId` (`src/Reservanto.ApiClient/Core/ApiClientLog.cs`). Bez osobních údajů a bez tokenů. Texty chyb z API mohou nést údaje zákazníka, proto jdou jen na úroveň Debug.

### A7. Konfigurace

Konfigurace jde přes options třídu s konstantou `SectionName` a rozumnými výchozími hodnotami (`AvailabilityViewOptions`, `ReservantoApiOptions`). `IConfiguration` se v `Application` nepoužívá. Klíče konfigurace jsou veřejné rozhraní pro merchanty (`docs/SPEC.md` sekce 5), jejich změna rozbije existující instalace.

---

## B. Styl C#

Většinu vynucuje build přes `.editorconfig`, `EnforceCodeStyleInBuild` a `TreatWarningsAsErrors` (`Directory.Build.props`).

### B1. Pojmenování

| Co | Konvence |
|---|---|
| Typy, metody, vlastnosti, konstanty | PascalCase |
| Rozhraní | prefix `I` |
| Privátní pole včetně statických | `_camelCase` |
| Async metody | přípona `Async` (mimo testy) |
| `CancellationToken` | parametr `ct` |
| Testy | `Subject_Scenario_Expectation` |

### B2. Jazykové prvky

- File-scoped namespace, `using` mimo namespace, `System` první.
- Složené závorky vždy, i u jednořádkového `if`. Závorka na novém řádku.
- `var` jen při zjevném typu, u vestavěných typů nikdy.
- `this.` se nepíše. Build to i přes `.editorconfig` nehlásí, hlídá to `CodeRules`.
- Modifikátor přístupu vždy, pole `readonly`, kde to jde.
- Porovnání s `null` přes `is null` a `is not null`.
- Kolekce přes výrazy `[...]`.
- Řetězce porovnávej přes `StringComparison.Ordinal` a formátuj s `CultureInfo.InvariantCulture`, když nejde o text pro návštěvníka. Hlídají to analyzery.
- `ConfigureAwait` se nepíše, v ASP.NET Core nemá smysl.
- Asynchronní metody předávají `ct` dál.

### B3. Soubory

- Související malé typy mohou být v jednom souboru: rozhraní s jeho `record` požadavky a výjimkami.
- Složky podle oblasti (`Availability/`, `Connection/`, `Security/`), ne podle technického druhu.
- UTF-8 bez BOM, konce řádků podle `.gitattributes`.
- `README.md` je česky, `README.en.md` anglicky se stejným obsahem. Změna jednoho se promítne do druhého.

### B4. Komentáře

- Komentáře česky, vysvětlují **proč**, ne co. Identifikátory a ustálené termíny anglicky.
- Komentář nese důvod sám. Neodkazuje na plán, evaluaci ani číslo milníku: čtenář je nemusí mít a komentář by zastaral. Odkaz na `docs/SPEC.md` nebo na datum v `docs/decisions.md` je v pořádku. Odkaz na milník v kódu hlídá `PublicContentRules`.
- XML doc je povinný jen u veřejného kontraktu, na kterém staví jiný kód (`AvailabilityView.Application`, vynucené přes CS1591). Jinde jen tam, kde kód sám nestačí. U implementace rozhraní `<inheritdoc />`.

---

## C. Projekt a build

- **.NET 10**, verze SDK v `global.json`, formát řešení `.slnx`.
- **`Directory.Build.props`:** společná pravidla pro všechny projekty, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `Nullable`, `ImplicitUsings`.
- **`Directory.Packages.props`:** centrální verze balíčků, jedna verze na balíček pro celé řešení, u balíčku komentář, proč tam je.
- **Analyzery:** jen vestavěné .NET. Další se přidá, až se třída chyb opakuje.
- **Build jen `dotnet build`.** Žádný webpack, vite ani esbuild. SCSS a TypeScript překládají MSBuild balíčky (`AspNetCore.SassCompiler`, `Microsoft.TypeScript.MSBuild`), Node pro překlad není potřeba.
- **Skripty běží ve Windows**, tedy PowerShell, Node nebo .NET. Vývoj nepotřebuje WSL.
- **Hosting:** Windows Server a IIS (`docs/instalace-iis.md`).
- **Vývojové balíčky** v přesné verzi starší než dva týdny.

---

## D. Web (ASP.NET Core)

- **Razor MVC**, ne Blazor. Kontrolery `sealed`, primární konstruktor, atributové routování.
- Kontroler neobsahuje byznys logiku a volá `Application`.
- **Lokalizace:** `.resx` a `IStringLocalizer<T>`, žádné natvrdo psané texty v UI. Test hlídá, že každý klíč v resx existuje.
- **Bezpečnost:** CSP bez `unsafe-inline` a `unsafe-eval`, skripty s nonce, omezení počtu požadavků, v logu žádné osobní údaje. `Html.Raw` jen s komentářem `// SAFE: <důvod>`, hlídá `CodeRules`.
- Cesty API zná jen `src/Reservanto.ApiClient/Core/ApiEndpoints.cs`, hlídá `CodeRules`.

---

## E. Frontend

- **TypeScript s ES moduly**, `strict`, `noUnusedLocals`, `noUnusedParameters`, `noImplicitReturns`. Jednoduché uvozovky, 4 mezery, středníky.
- **SCSS a CSS custom properties.** Třídy s prefixem `av-` a BEM (`.av-nav__link`), hodnoty přes proměnné `--av-*`, žádné inline styly, `!important` jen s komentářem důvodu. Třídy a proměnné stránky návštěvníka jsou veřejné rozhraní (`docs/vzhled.md`).
- **Přístupnost WCAG 2.1 AA**, kontroluje axe-core v testech v prohlížeči.
- **Nepoužívat:** React, Vue, Angular, Blazor, HTMX, jQuery, Bootstrap, Tailwind, CSS-in-JS, bundler. Ikony jako inline SVG.
- Stránka funguje i bez JavaScriptu, skript ji jen vylepšuje.

---

## F. Testy

- **xUnit v3 na Microsoft.Testing.Platform** (`global.json`).
- **Struktura:** jeden testovací projekt na projekt v `src/`, navíc `AvailabilityView.Architecture.Tests`, `AvailabilityView.Repository.Tests` (kontroly souborů v repozitáři) a `AvailabilityView.Web.E2E.Tests` (prohlížeč).
- **Pojmenování:** třída `<Subjekt>Tests`, metoda `Subject_Scenario_Expectation` anglicky. Komentáře česky a jen tam, kde test sám neřekne proč.
- **Mock knihovna žádná.** Ručně psané náhrady v `Support/` daného projektu. API se nemockuje: testy jdou přes skutečný klient a falešné API nad nahrávkami (`tests/api-recordings/README.md`).
- **Tři rychlosti:** rychlé testy bez sítě a bez prohlížeče při každé změně, testy s traitem `E2E` (prohlížeč) a `Smoke` (produkční API, jen čtení) před vydáním.
- **Žádný `Skip`.** Existující test se neupravuje jen proto, aby prošel. Změnu existujícího testu je potřeba zdůvodnit.
- **Každé pravidlo a kontrola má negativní test.** Porušení žije jen v testu jako fixtura, nikdy v `src/`.
- **Žádné pravidlo nesmí projít naprázdno:** vedle pravidla je test, že vidí aspoň jeden soubor, projekt nebo typ.
- **Číselný práh pokrytí není.** Povinně se pokrývá logika výpočtu volna, každá kontrola a každá nalezená chyba.
- **Prohlížeč:** Playwright v .NET a axe-core, Chromium se instaluje jednorázově (README).

Příkazy jsou v `eng/check.ps1`. Past: `dotnet test` v režimu Microsoft.Testing.Platform spouštěj **bez `--nologo`**, jinak skončí „spustila se nula testů“ (exit 5). Filtry patří za `--`.
