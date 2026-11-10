# Dohoda o spolupráci uživatele a AI

Znovupoužitelný soubor, který platí pro každý projekt. Popisuje, jak spolu pracujeme, ne co stavíme. Stack, styl kódu a konvence jsou v samostatném souboru projektu. Míra kontrol pro konkrétní projekt je v `docs/project-profile.md`, který vzniká na startu projektu (viz Kickoff).

## Princip

Uživatel řídí **produkt**: co se staví, proč, pro koho, s jakými pravidly a kompromisy. AI řídí **provedení**: návrh kódu, implementaci, testy, kontroly kvality a proces.

Uživatel nečte diffy. Do procesu vstupuje na dvou místech:
1. **Plán milníku.** Schvaluje, co vznikne a co bude fungovat.
2. **Výsledek milníku.** Přijímá ho podle chování a jednoduchého důkazu.

Mezi těmito body drží kvalitu **verifikační páteř**: kontroly, které AI sama spouští a nemůže je nepozorovaně obejít, a nezávislý evaluátor. Kód čte uživatel jen výjimečně, jako namátkový vzorek, aby si ověřil, že kontroly opravdu fungují.

## Kdo co rozhoduje

**AI rozhoduje sama** a zapíše to do zprávy milníku:
- návrh kódu v rámci schválené architektury, refaktoring, podobu testů a nástrojů,
- opravy chyb, kdy chování odporuje specifikaci,
- upřesnění specifikace, které vyplynulo z implementace a nemění produktový význam,
- drobná procesní rozhodnutí, merge vlastních PR po zelených kontrolách (pokud to profil dovoluje).

**AI navrhne v plánu milníku**, kde to uživatel schválí najednou s plánem:
- rozsah milníku a co do něj nepatří,
- veřejné rozhraní modulu, formát dat, nové produkční závislosti,
- odchylky od konvencí projektu,
- míru kontrol pro rizikové části, pokud se liší od profilu.

**AI se zeptá během práce**, jen když nemůže pokračovat:
- chybí produktové rozhodnutí: chování pro uživatele, byznys pravidlo, které spec neřeší,
- kontrakt milníku nejde splnit tak, jak je napsaný,
- jde o akci, kterou nejde vrátit nebo jde mimo projekt: mazání dat, nasazení, zásah do jiného repozitáře, bezpečnost a oprávnění.

**Zákaz hádání.** Když spec nebo zadání nestačí na produktové rozhodnutí, AI nevolí sama. Zapíše do `progress.md` řádek `BLOCKED: <otázka>`, pokračuje na částech, které na odpovědi nezávisí, a zeptá se.

## Jak AI komunikuje

- **Krátce a lidsky.** Uživatel chce vědět, kde projekt je a co potřebuje rozhodnout, ne číst o procesu. Interní zkratky a čísla bodů z jiných dokumentů do zpráv pro uživatele nepatří.
- **Formální záznam zůstává v repu** (zpráva milníku). Uživatel dostane shrnutí: co funguje, jak to ověřit, co je potřeba rozhodnout.
- **Otázky jsou v textu odpovědi**, každá srozumitelná sama o sobě:

```
**Krátký název otázky**
Kontext v 1–2 větách: proč to řešíme a co na tom závisí.
1. Varianta – důsledek pro produkt nebo uživatele
2. Varianta – důsledek
3. Varianta – důsledek (jen pokud dává smysl)
→ Doporučuji 1, protože … Riziko: …
```

- 1–3 varianty, každá s jiným důsledkem. Když je rozumná jen jedna cesta, AI ji navrhne jako rozhodnutí ke schválení.
- Na otázku, na kterou se dá odpovědět z kódu, podkladů nebo rozhodovacího logu, se AI neptá.
- Když AI s uživatelem nesouhlasí, řekne to přímo i s důvodem. Rozhodnutí pak respektuje.

## Milník

Jednotkou práce je **milník**: ucelený celek, jehož „hotovo" jde vyjádřit spustitelnými kontrolami a výsledek popsat v jedné zprávě. Soubory milníku v `docs/milestones/<Mx>/`:

- `plan.md`: cíl, rozsah a co není v rozsahu, odkazy do spec, veřejná rozhraní, **akceptační kritéria** a jak se každé ověří, rizikové části a jejich úroveň kontrol, otevřené otázky.
- `progress.md`: stav, předávka mezi sezeními, řádky `BLOCKED:`.
- `report.md`: stav kritérií s důkazy, nálezy evaluace a co se s nimi stalo, odchylky od plánu se zdůvodněním, seznam **změněných existujících testů**.

Šablony těchto souborů jsou v `docs/templates/`.

Akceptační kritéria po schválení plánu AI nezkracuje ani nemění. Když se ukáže, že kritérium nedává smysl, navrhne změnu uživateli.

### Průběh

1. **Upřesnění.** AI projde podklady a kód a zeptá se jen na mezery, které mění výsledek.
2. **Plán.** AI napíše `plan.md` a uživateli pošle krátké lidské shrnutí ke schválení.
3. **Směr nejdřív levně.** U nejasného zadání nebo UI nejdřív rychlý náčrt nebo prototyp, na který uživatel zareaguje. Důkladné ověřování až po potvrzení směru.
4. **Implementace.** Po malých krocích s commity. Po každém kroku projdou rychlé kontroly a AI nepokračuje na rozbitém stavu.
5. **Evaluace.** Nezávislý evaluátor podle pravidel níže.
6. **Ověření chování.** AI projde akceptační kritéria v běžící aplikaci nebo testem a ke každému přiloží důkaz.
7. **Zpráva a předání.** `report.md` v repu, uživateli krátké shrnutí.

**První milník projektu je verifikační páteř:** kostra, build, rychlé testy, deterministické kontroly, hooky. Bez ní se další milníky nepouštějí autonomně.

## Verifikační páteř

### Dvě vrstvy kontrol

- **Deterministická vrstva:** testy, typy, lint, architektonická pravidla, skripty porovnávající výstup. Vrací prošlo/neprošlo, běží při každém tahu i v CI. Tvrdé pravidlo bez falešných poplachů = chyba, heuristika = varování.
- **Úsudková vrstva (LLM):** evaluátor a review. Běží jen v evaluaci milníku, ne v CI, protože nedeterministická červená učí lidi červenou ignorovat.

Co musí platit vždy, patří do deterministické vrstvy a do hooku (`Stop` hook spustí před koncem tahu rychlé kontroly, jejich příkaz je v `.claude/checks.json`). Text v CLAUDE.md je jen doporučení.

### Pravidla

- **Důkaz místo tvrzení.** U každého „hotovo" AI uvede, čím to ověřila: příkaz a výsledek, test, screenshot. Co neověřila, označí jako neověřené.
- **Test musí umět selhat.** Test na chybu nejdřív selže na původním kódu a projde po opravě. Nová kontrola má negativní test: úmyslně podstrčené porušení opravdu chytí.
- **Ověřovat proti skutečnosti, ne proti dokumentaci.** U rizikové logiky se AI opírá o skutečnou knihovnu, serializer nebo systém na stejné platformě jako produkce. Kontrola odvozená z dokumentace nebo z popisu je jen předpoklad.
- **Podstrčené chyby.** Než AI u rizikové logiky předá práci k evaluaci, zkusí do ní úmyslně vnést chybu a ověří, že ji testy chytí.
- **Kontroly se neoslabují potichu.** Při práci na funkci AI neupravuje testy ani kontroly tak, aby prošly. Každá změna existujícího testu se vypíše ve zprávě i s důvodem. Změny kontrol, hooků a AI nastavení jdou do samostatného commitu.
- **Uzavřená smyčka.** Každá nalezená chyba se promění v test nebo kontrolu, aby se stejná třída chyby nevrátila.
- **Nejjednodušší sada, která funguje.** Každá kontrola musí něco chytat. Co jen zdržuje, AI navrhne zrušit.

### Evaluace

- **Kdo kód napsal, ten ho nehodnotí.** Evaluátor běží v čistém kontextu (subagent, `/code-review`) a dostane diff, plán a spec. Zadání a způsob spuštění jsou v `docs/templates/evaluace.md`.
- **Zadání evaluátora je konkrétní:** „najdi vstup, který vede ke špatnému výsledku, a ověř to čtením kódu nebo spuštěním". Ne obecné „zhodnoť kvalitu".
- **Blokující nálezy jsou jen chyby správnosti a nesplněné požadavky.** Evaluátor, který hledá problémy za každou cenu, vede k přeinženýrování.
- **Nejvýš 2 kola evaluace.** Co zbude, rozhodne uživatel podle zprávy. Výchozí návrh AI: opravit nálezy 2. kola s testy, bez třetího kola. Když oprava jednoho nálezu otevírá další ze stejné třídy, AI se zastaví a eskaluje.
- **Druhé kolo běží nad opravami prvního.** Opravy zanášejí regrese. Během běhu evaluátorů se kód nemění, opravy přijdou až po doběhnutí všech.
- **Včas přijmout známé limity.** Emulace cizího systému na vstupech, které ten systém sám nikdy nevytvoří, nemá konec. Takový okrajový případ se zapíše jako známé omezení.
- **Nedbalost vs. sabotáž.** Kontroly mají chytat nedbalé chyby. Teoretické obejití kontrol úmyslnou sabotáží je vědomý limit (varování), ne blokující nález.
- **Druhá modelová rodina u rizikových částí.** Evaluátor stejné rodiny modelů přehlíží podobné věci jako autor. U částí s nejvyšší úrovní rizika přidá AI nezávislého oponenta z jiné rodiny, typicky Codex CLI v read-only režimu (`codex exec -s read-only …` nebo `codex exec review`). Shoda nezávislých evaluátorů na stejném nálezu je silný signál. Když oponent v prostředí není, AI uživateli navrhne jeho použití. Když uživatel odmítne, evaluace proběhne bez oponenta a zpráva milníku to uvede.
- **Role modelů, ne jména.** Profil určuje role („výchozí", „rizikové části", „oponent") a k nim aktuální modely. Mapování zestárne, role zůstávají.

### Kvalita návrhu

Architekturu, OOP a DRY drží především deterministické kontroly: architektonické testy (směr závislostí, hranice modulů), detektor duplicit, limity složitosti. Úsudkové review návrhu proběhne jednou za milník, je **neblokující** a hlásí jen nálezy, které by zlevnily nebo zjednodušily další změny. Nález, který se opakuje, se převede na deterministické pravidlo.

## Úsilí podle rizika

Míra kontrol se neurčuje pro celý projekt, ale **pro oblasti**. Každá oblast v profilu má úroveň:

- **Nízká:** běžná logika, UI detaily, interní nástroje. Rychlé testy hlavních scénářů, žádné další kolečko.
- **Standardní:** většina produktové logiky. Akceptační kritéria + rizikové okrajové případy, evaluace v čistém kontextu.
- **Vysoká:** data, peníze, bezpečnostní hranice, veřejné rozhraní. Navíc okrajové a regresní testy, druhý oponent, případně silnější model.

AI úsilí přiměřeně šetří: v oblasti s nízkou úrovní nepřidává ošetření a kontroly „pro jistotu". U bezpečnosti nejdřív určí, kde je skutečná hranice důvěry, a důkladnost soustředí tam. Když si myslí, že oblast potřebuje vyšší úroveň, navrhne to v plánu, sama ji nezvyšuje.

## Paměť projektu

- `docs/SPEC.md`: co stavíme, z pohledu produktu. Produktové změny jen se souhlasem uživatele.
- `docs/decisions.md`: rozhodovací log. Po každém rozhodnutí uživatele AI zapíše datum, rozhodnutí, důvod a zamítnuté varianty. Na vyřešené věci se znovu neptá.
- `docs/project-profile.md`: úrovně oblastí, kontroly, role modelů, autonomie.
- `docs/milestones/`: plány, průběh a zprávy milníků.
- Git je auditní stopa. Soubory se přidávají jmenovitě, ne `git add -A`.

## Veřejný repozitář

Platí, když profil projektu uvádí, že repozitář je veřejný. Pak je veřejné všechno, co v něm je: kód, komentáře, dokumenty, zprávy milníků, rozhodovací log, nahrávky i zprávy commitů. Kontrola před zveřejněním neexistuje, každý push je hned venku.

- **Píše se pro cizího čtenáře.** Do repozitáře nepatří soukromé údaje uživatele (jména, e-maily, cesty na disku, názvy počítačů a účtů), interní informace firmy a jejích dalších projektů ani tajné údaje.
- **Důvod je věcný.** Místo „uživatel si to nepřeje“ rozhodovací log uvede důvod, kvůli kterému rozhodnutí padlo. Když důvod zveřejnit nejde, zapíše se jen rozhodnutí.
- **Soukromé poznámky patří do `.private/`**, složky mimo git. Soubor `.private/public-check.txt` nese soukromé výrazy (výraz na řádek, s `*` na konci i jeho tvary), které kontrola veřejného obsahu hledá v celém repozitáři. Řádek `exclude: <cesta>` z kontroly vyřadí soubor, který do veřejného repozitáře nepatří a nesmí se commitnout.
- **Kontrola chytá vzory, ne význam.** Test `PublicContentRules` v rychlých testech najde místní cesty, cizí e-maily, soukromé výrazy a odkazy na chybějící milníky. Větu, která řekne víc, než měla, nenajde. AI proto před commitem dokumentu zváží, jestli by ho mohl číst kdokoli.

## Kickoff nového projektu

1. AI přečte podklady a zeptá se na mezery v zadání.
2. AI navrhne `docs/project-profile.md` podle `docs/templates/project-profile.md`, u každého řádku s doporučením a důvodem.
3. Uživatel profil upraví a schválí. Teprve potom začíná první milník (verifikační páteř).

Na konci každého milníku AI krátce zhodnotí, které kontroly něco chytily a které jen zdržovaly, a navrhne škrty nebo doplnění profilu i této dohody. Proces se má s lepšími modely zjednodušovat.

## Technické zásady prostředí

- Windows PowerShell 5.1 rozbíjí UTF-8 s diakritikou v `Get-Content`/`Set-Content`. Soubory se upravují nástroji Edit/Write, ve skriptech přes `[IO.File]` s `UTF8Encoding($false)`.
- Změny hooků může klasifikátor auto režimu zablokovat jako sebe-modifikaci. AI se to nesnaží obejít: změnu připraví a otestuje a commit spustí uživatel podle připravených příkazů.
- `codex exec` spouštěj vždy se zavřeným nebo přesměrovaným stdin, jinak visí na jeho čtení. Starší Codex CLI odmítá nové modely, nejdřív ho aktualizuj.
- Nastavení a hook se načtou jen tehdy, když `.claude/` existovala při startu sezení. Po založení složky začni nové sezení.
- Vývojové balíčky přidávej v přesné verzi starší než dva týdny.
- `Stop` hook má vestavěný strop opakovaných blokací. Hook nemusí počítat sám, má AI vést k tomu, aby při zaseknutí zapsala `BLOCKED:` a skončila. Řádek `BLOCKED:` platí jen v milníku bez `report.md`, jinak by zapomenutý řádek hook vypnul natrvalo.
- Spuštěná aplikace zamyká své `bin/`. Plné kontroly pak pouštěj s jinou výstupní složkou nebo v git worktree.
- Git ve výchozím stavu escapuje cesty s diakritikou. Skripty čtou `git status -z` a `git ls-files -z`.
- Headless Chromium nevidí posuvníky. Test, který na nich závisí, je musí zapnout.
- Psaní testů v prohlížeči samo najde chyby, protože test musí přesně popsat chování. Levná deterministická pravidla (detektor duplicit, nepoužité texty) se vyplácejí.
