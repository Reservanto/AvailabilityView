# AvailabilityView: specifikace první verze

Co stránka dělá a jak se chová. Rozhodnutí a jejich důvody jsou v `docs/decisions.md`.

## 1. Účel

Stránka jen pro čtení, která návštěvníkům webu merchanta ukazuje jeho volnou kapacitu z Reservanta. Vkládá se přes iframe nebo se otevírá odkazem. Jedna instance obsluhuje jediného merchanta. Projekt nemá databázi ani uživatelské účty a nepoužívá cookies, takže na webu merchanta nepotřebuje souhlas s cookies.

## 2. Kdo to používá

- **Návštěvník** si prohlíží volné termíny.
- **Merchant** si v průvodci připojí Reservanto účet, sestaví si odkaz nebo kód iframe a případně dodá vlastní CSS.
- **Reservanto** hostuje instance na svém serveru (IIS). Technicky zdatnější merchant si může instanci hostovat sám.

## 3. Co návštěvník uvidí

| Zobrazení | Parametr | Pro koho | Podoba |
|---|---|---|---|
| Agenda | `view=agenda` | všechny podporované typy | Seznam termínů po dnech |
| Kalendář | `view=calendar` | všechny podporované typy | Týden ve sloupcích dnů, přepínání týdnů |
| Hodinová osa | `view=timegrid` | všechny podporované typy | Týden s hodinovou osou, přepínání týdnů |
| Měsíc | `view=month` | všechny podporované typy | Měsíc od pondělí se stavem dne, přepínání měsíců |
| Nejbližší termín | `view=next` | všechny podporované typy | Jedna věta s nejbližším volným termínem, třeba pro stránku produktu |

Merchant v `view` vyjmenuje jedno nebo víc zobrazení. První je výchozí, při více zobrazeních je nad termíny přepínač.

**Měsíc** ukáže každý den jako volný, plný, zavřený nebo mimo nabídku, bez půlených dnů. U lekcí je den plný, když jsou plné všechny lekce, a den bez lekcí je „bez lekcí“. U individuálních typů je den plný, když zdroje pohledu pracují a volno nemají, a zavřený, když žádný nepracuje. Pracovní doba se pro měsíc čte bez rezervací. Den s volnem odkazuje na svůj týden, když `view` obsahuje kalendář nebo hodinovou osu.

**Nejbližší termín** je věta, třeba „Nejbližší volný termín: zítra 9:00“. U lekcí je to první lekce, která není plná, s názvem služby, pokud ji neurčuje adresa. U individuálních typů začátek prvního volného úseku od teď. Když v období `days` nic není, věta řekne, do kdy nic volného není.

Obsazenost se ukazuje takto:

- **Lekce:** u každého termínu čas od–do, název služby a zdroje a text, který si merchant nastavil v Reservantu (např. „2/10“ nebo „volno“). Plně obsazené termíny zůstávají vidět, označené jako obsazené.
- **Individuální zaměření:** kdy mají zdroje volno, jako úseky od–do po zdrojích. Zdroj má volno v čase, kdy u něj jde začít aspoň jednu veřejnou službu pohledu. Úsek přes půlnoc se ukáže po dnech, konec dne jako 24:00. Název služby se neukazuje, jen když ji merchant v adrese určí, je jednou nad termíny. Na hodinové ose jsou navíc v pracovní době zdroje jeho rezervace označené jako obsazené a zbytek jako nedostupný, mimo pracovní dobu se nic nekreslí. Volno má přednost před obsazeností, protože zdroj s kapacitou pro víc lidí může mít volno i během rezervace. Bez určeného zdroje je úsek volný, když je volný aspoň jeden zdroj.

Stránka neobsahuje odkaz na rezervaci ani ceny.

## 4. Typy zaměření a zdroj dat

| Typ v API | Co to je | Čtení | Povinné vstupy |
|---|---|---|---|
| `Classes` | lekce s pevným termínem | `Classes/GetAvailableAppointments` | zaměření, provozovna |
| `OneToOne` | individuální služby | `OneToOne/GetAvailableStartsForLocation` pro každou veřejnou službu pohledu, volno se sjednotí. Pro hodinovou osu navíc `WorkingHours/GetForPeriod` a rezervace z `Event/GetList` za zobrazený týden | provozovna. Zdroj se vybere z výsledku |
| `AutoClasses`, `EmsLike` | chovají se jako individuální | stejně jako `OneToOne` | stejně jako `OneToOne` |

Ostatní typy, například `RentalLike`, `CoursesLike`, `PlaceRentalLike`, `AccommodationLike`, `RestaurantsLike` a `SchoolsLike`, v první verzi nejsou. Generátor je nenabízí a stránka u nich napíše, že zobrazení není dostupné.

`AutoClasses` a `EmsLike` jsou ověřené na produkčním API 2026-11-06. `OneToOne/GetAvailableStarts` je odmítá, proto se všechny tři typy čtou jen přes `OneToOne/GetAvailableStartsForLocation`.

**Co chybí v URL.** Stránka vždy něco zobrazí a nečeká na volbu návštěvníka. Chybějící zaměření nebo provozovna se nahradí první veřejnou možností v pořadí z API (u zaměření první podporovaného typu). Chybějící služba se nedoplňuje: u lekcí se zobrazí lekce všech služeb provozovny, u individuálních typů volno ze všech veřejných služeb zaměření v provozovně. Když zdroj není určený, zobrazí se termíny všech zdrojů, které splňují ostatní filtry. Když v daném období nic volného není, stránka to napíše.

## 5. Nastavení

### Konfigurační soubor

Přístupové údaje a věci, které nesmí měnit návštěvník. Soubor `appsettings.Production.json` na serveru, ve vývoji user-secrets. Nikdy v repozitáři.

| Klíč | Výchozí | Význam |
|---|---|---|
| `Reservanto:BaseUrl` | `https://api.reservanto.cz/` | adresa API |
| `Reservanto:ClientId` | žádný | nepovinné, předvyplní pole v průvodci |
| `Reservanto:LongTimeToken` | žádný | přístup k účtu merchanta, tajný údaj |
| `AvailabilityView:AllowedFrameOrigins` | prázdné | seznam webů, které smí stránku vložit přes iframe, ve tvaru `https://www.example.cz`: schéma, doména, volitelně port, bez cesty a bez hvězdičky. Neplatná položka nedovolí start aplikace, změna platí po restartu |
| `AvailabilityView:CustomCssUrl` | žádné | vlastní CSS merchanta: absolutní adresa `https://…` nebo cesta od kořene instance (`/custom/web.css`). Načte se za styly aplikace jen na stránce návštěvníka, CSP povolí styly a písma jen z jeho originu. Neplatná hodnota nedovolí start aplikace, změna platí po restartu. Podrobnosti v `docs/vzhled.md` |
| `AvailabilityView:TimeZone` | `Europe/Prague` | API časové pásmo merchanta nevrací |
| `AvailabilityView:DefaultLanguage` | `cs` | výchozí jazyk |
| `AvailabilityView:MaxDays` | `60` | nejdelší období, které jde zobrazit |

### Parametry URL

Co se má zobrazit. Stejná instance tak může mít jiný pohled pro každou stránku webu.

| Parametr | Význam |
|---|---|
| `view` | jedno nebo víc z `agenda`, `calendar`, `timegrid`, `month`, `next` oddělených čárkou, první je výchozí. Výchozí `agenda` |
| `segment` | Id zaměření |
| `location` | Id provozovny |
| `resource` | Id zdroje |
| `service` | Id služby |
| `pickers` | které filtry si návštěvník přepíná sám, např. `location,service` |
| `days` | kolik dní dopředu, výchozí 14. V kalendáři určuje, kam až jde přepnout |
| `week` | jen kalendář a hodinová osa: datum `RRRR-MM-DD`, zobrazí se týden, do kterého patří. Výchozí je aktuální týden |
| `month` | jen měsíc: `RRRR-MM`. Výchozí je aktuální měsíc |
| `lang` | `cs`, `en`, výchozí z `AvailabilityView:DefaultLanguage` |

Parametry může návštěvník ručně změnit. Dostane se tak jen k veřejným datům, ale skrýt veřejnou provozovnu URL neumí. Neplatná nebo neveřejná hodnota se chová jako neexistující: stránka napíše, že pohled není dostupný, a neprozradí, jestli daný objekt existuje.

Výjimky: neznámý `lang` dá výchozí jazyk, jazyk prohlížeče se nebere v úvahu. Datum `week` nebo `month` mimo nabízené období se posune na nejbližší nabízený týden nebo měsíc.

Prázdná hodnota (`resource=`) je stejná jako chybějící, formulář filtrů ji posílá za volbu „všechny“. Neplatný je opakovaný parametr, hodnota s mezerou nebo znaménkem, jiný tvar data než `RRRR-MM-DD` (u `month` než `RRRR-MM`) a v seznamech neznámá, prázdná nebo opakovaná položka. Hodnoty se píší malými písmeny. Neznámé parametry, třeba z měření kampaní, se nechají být.

Hodnota filtru z `pickers` je jen výchozí. Filtry na sobě závisí v pořadí zaměření, provozovna, služba, zdroj. Když hodnota k vyšším volbám nepatří, zahodí se a doplní znovu, aby změna zaměření nevedla na „pohled není dostupný“. Hodnota mimo `pickers` je pevná a omezuje i nabídku vyšších filtrů.

Kalendář ukazuje celé týdny od pondělí, v obou jazycích. Dny před dneškem a za koncem období `days` jsou označené jako mimo nabídku. Přepnout jde jen na týdny, které s obdobím mají společný aspoň jeden den. Měsíc se chová stejně: dny mimo období jsou označené, přepnout jde jen na měsíce v období a `month` mimo něj se posune na nejbližší nabízený měsíc. `days` je zároveň horizont: merchant ho nastaví na dobu, jak daleko dopředu jde rezervovat, jinak měsíc ukáže dny za horizontem jako plné. Angličtina se formátuje britsky (`en-GB`), čeština podle `cs-CZ`.

Stránka funguje i bez JavaScriptu: filtry jsou formulář, týdny odkazy.

Vlastní CSS je záměrně v souboru, ne v URL. Přes parametr by kdokoli mohl podstrčit cizí vzhled na adrese merchanta.

## 6. Připojení k Reservantu

Každý merchant si registruje vlastní ClientId s návratovou adresou své instance (e-mailem na obchod@reservanto.cz). Instance hostované Reservantem mohou sdílet jeden ClientId, protože registrace dovoluje víc adres.

Připojením provede správce průvodce na stránce `/Setup`. Nepřipojená instance na něj odkáže z hlavní stránky.

1. **ClientId.** Průvodce vysvětlí, jak ho získat, a ukáže návratovou adresu této instance ke zkopírování do registrace. Správce ClientId vloží do pole na stránce.
2. **Povolení přístupu.** Průvodce vypíše žádaná práva srozumitelnými slovy. Tlačítko vede na souhlas v Reservantu, kde se správce přihlásí a přístup povolí.
3. **Uložení tokenu.** Reservanto vrátí správce na `/Auth/Callback`. Stránka mu ukáže token, tlačítko pro zkopírování a přesně napíše, kam ho vložit: na serveru jako část `appsettings.Production.json`, ve vývoji jako příkaz pro user-secrets.
4. **Ověření.** Instance změnu konfigurace načte sama. Průvodce zavolá API a ukáže název připojeného účtu merchanta a kolik veřejných zaměření a provozoven vidí. Když je zamítnutý souhlas, neplatný token nebo výpadek API, napíše co s tím.
5. **Pokračování.** Průvodce nabídne nastavení povolených domén a přechod do generátoru.

Průvodce je česky. Instance token sama neukládá. Nepotřebuje tak právo zápisu na disk a cizí člověk nemůže podstrčit jiný účet. Kdo projde souhlasem, vidí jen token ke svému vlastnímu účtu.

Kroky 1 až 3 fungují jen dokud v konfiguraci token není. Připojená instance na `/Setup` ukáže jen stav a odkaz do generátoru. Bez tokenu aplikace normálně nastartuje.

**Žádaná práva:** `Segment_r`, `Location_r`, `BookingResource_r`, `BookingService_r`, `FreeSpace_r`, `Merchant_r`. **Jen s volbou obsazenosti:** `Event_r`. Žádná práva k zákazníkům ani k zápisu. `Merchant_r` slouží jen k názvu účtu v ověření, ostatní údaje merchanta (IČO, kontakty, adresy) stránka nepoužívá.

**Obsazenost je volba správce.** Průvodce nabídne volbu „Ukazovat obsazenost na hodinové ose“, standardně vypnutou. Jen se zapnutou volbou souhlas žádá `Event_r`. To slouží jen k obsazeným časům na hodinové ose: z rezervace se bere začátek, konec, zdroj a stav, údaje zákazníků se zahodí při načtení. Token bez `Event_r` není chyba: průvodce napíše, že obsazenost je vypnutá, a hodinová osa ukáže v pracovní době jen volno a zbytek jako nedostupný. Instance to pozná z odpovědi API, v konfiguraci na to klíč není. Odmítnuté čtení rezervací se nesmí plést s výpadkem API.

**Vývoj:** běží na `https://localhost:5001`, návratová adresa je `https://localhost:5001/Auth/Callback`.

## 7. Generátor odkazu

Stránka `/Generator` načte veřejná zaměření, provozovny, zdroje a služby. Merchant v ní vybere pohled, vidí živý náhled a zkopíruje si odkaz nebo kód iframe. Nic neukládá a pracuje jen s veřejnými daty, proto nepotřebuje přihlášení ani tajnou adresu.

- Generátor je česky a funguje i bez JavaScriptu: formulář se odešle tlačítkem, se skriptem hned po změně volby. Zkopírovat odkaz a kód jde jen se skriptem, bez něj jde text označit.
- Nabídka je stejná jako u filtrů stránky: jen veřejné možnosti podporovaných typů zaměření, které k sobě patří. Nižší volba se řídí vyššími, nepatřící hodnota se nahradí.
- Volby: výchozí zobrazení a další zobrazení k přepnutí (`view`), zaměření, provozovna, služba, zdroj, filtry návštěvníka (`pickers`), `days` a `lang`.
- Zaměření a provozovna jsou v odkazu vždy pevné podle náhledu, aby se pohled nezměnil, když merchant přidá další zaměření. Výchozí hodnoty (`view=agenda`, `days=14`, výchozí jazyk instance) se do odkazu nepíšou.
- Kód pro vložení je `<iframe>` s odkazem, názvem v jazyce stránky a výškou 600 px a skript `embed.js`, který výšku přizpůsobí obsahu.
- Adresa instance v odkazu je adresa, na které správce generátor otevřel.
- Nepřipojená instance odkáže na `/Setup`. Generátor nejde vložit do iframe.

## 8. Vložení a vzhled

- Výška iframe se přizpůsobuje obsahu. Kód z generátoru obsahuje skript `embed.js` z instance, bez něj má iframe pevnou výšku 600 px. Adresu skriptu a formát zprávy o výšce popisuje `docs/vzhled.md`.
- Vložení povoluje hlavička `frame-ancestors` podle `AllowedFrameOrigins`, jen u stránky návštěvníka včetně hlášek. Vlastní instance ji vložit smí kvůli náhledu v generátoru. Průvodce, generátor a návrat ze souhlasu vložit nejde nikdy. Otevření přes odkaz funguje vždy.
- Vlastní CSS se načítá po stylech aplikace. Barvy, písma a mezery jsou v CSS proměnných. Proměnné a názvy tříd jsou zdokumentované a merchanti na nich staví, takže se nemění bez upozornění.
- Stránka nemá hlavičku ani patičku, aby zapadla do webu merchanta.

## 9. Chování za běhu

- Volání API probíhá jen ze serveru. Do prohlížeče jde hotová stránka.
- Volné termíny a obsazenost z rezervací se drží v paměti 60 sekund, seznamy zaměření, provozoven, zdrojů a služeb a pracovní doba zdrojů 1 hodinu. Návštěvník tedy může vidět termíny až minutu staré a změna v seznamech se projeví nejpozději za hodinu.
- Počet požadavků je omezený podle IP adresy.
- Při výpadku API stránka napíše, že dostupnost teď nejde načíst. Stará data po vypršení cache neukazuje.
- Časy se zobrazují v časovém pásmu z konfigurace bez ohledu na prohlížeč a server.

## 10. Bezpečnost

- Token se nesmí dostat do repozitáře, logu ani do prohlížeče, s výjimkou jednorázové stránky `/Auth/Callback`.
- Reservanto vrací token v adrese, proto se `/Auth/Callback` nesmí logovat ani v aplikaci, ani v IIS.
- Údaje zákazníků z rezervací (jména, kontakty, poznámky, ceny, vlastní pole) se zahodí při načtení odpovědi. Nedostanou se do paměti aplikace mimo čtení, do cache, logu, textu chyb, nahrávek API ani na stránku.
- Na stránku jdou jen vyjmenovaná pole z veřejných objektů. E-maily a telefony zdrojů nikdy. Z údajů merchanta jen název, a to jen na `/Setup`. Termín u neveřejného zdroje nebo služby se nezobrazí vůbec.
- Přísná CSP bez `unsafe-inline`, skripty s nonce. Styl z `CustomCssUrl` je povolený výslovně.

## 11. Technologie

Podle `docs/conventions.md`: .NET 10, ASP.NET Core Razor MVC, TypeScript a SCSS bez frameworků, xUnit v3, Playwright, hosting na IIS.

Projekty: `AvailabilityView.Web`, `AvailabilityView.Application`, `AvailabilityView.Infrastructure`, `Reservanto.ApiClient`. Čtení dat je rozdělené po typech zaměření (osa variability podle pravidla A3 konvencí), zobrazení je společné.

`Reservanto.ApiClient` je klient Reservanto API jen pro čtení. Jako jediný přináší produkční závislost `Newtonsoft.Json`, na které stojí jeho převod odpovědí API.

## 12. Vývoj

První verze je hotová. Další práce se plánuje po milnících v `docs/milestones/` podle `docs/ai-working-agreement.md`. Změna produktového chování nebo veřejného rozhraní (parametry URL, klíče konfigurace, CSS proměnné a třídy) se nejdřív zapíše sem.

## 13. Mimo rozsah první verze

Rezervace a odkaz na ni, ceny, půjčovny (`RentalLike`), kurzy včetně `view=courses` (`CoursesLike`), `PlaceRentalLike` a `AccommodationLike`, Docker, více merchantů v jedné instanci, další jazyky (merchant si je doplní překladem), administrace s přihlášením.

## 14. Známá omezení

- Volno individuálních zdrojů je odvozené z volných začátků služeb. Volná mezera kratší než nejkratší služba pohledu se ukáže jako nedostupná. Úsek nedostupný na hodinové ose je přestávka, taková mezera, pauza po rezervaci, rezervace čekající na potvrzení, nebo rezervace, když token nemá `Event_r`.
- `Event/GetList` vrací jen potvrzené rezervace, čekající na potvrzení ne. Konec rezervace nezahrnuje pauzu po ní. Volné začátky obojí respektují, takový čas proto nikdy není volný, jen se ukáže jako nedostupný místo obsazeného (ověřeno 2026-11-09).
- Každá veřejná služba pohledu je jedno volání API za minutu a pohled. Přímé čtení volna zdroje (`BookingResource/GetAvailability`) se nehodí: na produkčním API vrací „dostupný“ i mimo pracovní dobu a v obsazeném čase (ověřeno 2026-11-08).
- `OneToOne/GetAvailableStartsForLocation` vrací volné začátky jen tak daleko dopředu, jak dovoluje nastavení merchanta, i když je žádané období delší. Individuální zaměření proto nemusí ukázat celé období z parametru `days`.
