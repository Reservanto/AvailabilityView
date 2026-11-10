# Rozhodovací log

Produktová a projektová rozhodnutí, která dál platí: datum, rozhodnutí, důvod a zamítnuté varianty. Nejnovější nahoře. Rozhodnutí, které nahradilo starší, je zapsané v jeho aktuální podobě.

## 2026-11-10

- **Oponent z jiné rodiny modelů je podmíněný.** Když Codex v prostředí není, AI navrhne jeho použití. Při odmítnutí evaluace proběhne bez oponenta a zpráva milníku to uvede.

## 2026-11-09

- **Stránka návštěvníka povolí písma z instance a z webu, odkud je vlastní CSS** (`font-src 'self' <origin stylu>`). Merchant tak může v rámu použít písmo svého webu, když ho má na stejném webu jako CSS. Soubor s písmem nespustí kód, CSP se rozšíří jen o web, který správce sám povolil. Obrázky dál jen z instance. Zamítnuto: žádná webová písma (merchant by měl jen systémová), další adresy písem a obrázků v konfiguraci (nový klíč veřejného rozhraní).
- **Měsíc rozliší volno, plno, zavřeno a mimo nabídku.** U individuálních typů se pro měsíc čte pracovní doba zdrojů (cache 1 h): den s pracovní dobou bez volna je plný, den bez pracovní doby zavřený. U lekcí je plný den, kde jsou všechny lekce plné, a den bez lekcí je bez lekcí. Důvod: zavřený den by jinak vypadal jako plný. Zamítnuto: volno / bez volna bez rozlišení, jen volno a mimo nabídku.
- **`view=next` ukáže jeden nejbližší volný termín jednou větou.** Hodí se třeba na stránku produktu v e-shopu. Krátký seznam umí agenda s malým `days`. Zamítnuto: několik termínů s parametrem počtu.
- **Horizont rezervací určuje merchant parametrem `days`.** API horizont nevrací (ověřeno 2026-11-09). Dny za ním jsou mimo nabídku ve všech zobrazeních. Zamítnuto: zvláštní parametr `horizon`, dělal by totéž.
- **Adresy proxy v konfiguraci zatím nejsou.** Za proxy vidí omezení počtu požadavků jednu IP, návod pro IIS to uvádí jako známé omezení. Klíč se přidá, až ho bude potřeba.
- **Volný úsek přes půlnoc se ve všech zobrazeních rozdělí po dnech.** Neděle 22:00–24:00, pondělí 0:00–24:00, úterý 0:00–2:00. Návštěvník hledá volno podle dne a všechna zobrazení ukážou totéž. Konec dne se píše 24:00. Zamítnuto: jeden úsek v den začátku s dnem konce (dny uprostřed by zůstaly v kalendáři prázdné).
- **Zobrazení jsou agenda, kalendář, hodinová osa, měsíc a nejbližší termín.** Jsou to hodnoty `view`, merchant si je vybere odkazem pro každou stránku svého webu.
- **Stránka neobsahuje odkaz na rezervaci.** Merchant má na webu vlastní cestu k objednání. Volitelný odkaz „Rezervovat“ může přijít v další verzi. Zamítnuto: odkaz z konfigurace instance.
- **Obsazenost z rezervací je volba správce, standardně vypnutá.** Průvodce nabídne „Ukazovat obsazenost na hodinové ose“ a jen podle ní žádá právo `Event_r`. Token bez něj není chyba, hodinová osa ukáže jen volno a nedostupné. Instance to pozná z odpovědi API, bez klíče v konfiguraci. Důvod: instance nemá mít přístup k rezervacím s údaji zákazníků, když je nevyužije. Zamítnuto: volba standardně zapnutá, žádat `Event_r` vždy.
- **Obsazenost z rezervací je jen na hodinové ose.** Agenda a sloupce dnů ukazují jen volné úseky a rezervace pro ně nečtou. Zamítnuto: obsazené úseky i v agendě.
- **Nahrávky rezervací obsahují jen pole, která aplikace čte.** Nahrávač ostatní odstraní, kontrola nahrávek shodí nahrávku s jiným polem. Zamítnuto: rezervace nenahrávat, nahrávat celé.
- **Hodinová osa na úzké šířce je pruh na den.** Každý den jeden vodorovný řádek, celý týden se vejde na obrazovku. Zamítnuto: dny pod sebou s vlastní svislou osou, stránka by měla asi 1600 px.

## 2026-11-08

- **Merchant si nastaví, co návštěvník uvidí, přes parametry URL.** Víc zobrazení najednou s přepínačem, rozložení individuálních termínů, zbytek přes další parametry a vlastní CSS. Zamítnuto: přepínač pro každý prvek stránky, příliš mnoho trvalých parametrů.
- **Kalendář s hodinovou osou je v první verzi**, hlavně pro individuální služby. U nich se název služby nezobrazí, jen volné a nevolné časové úseky.
- **Nevolné úseky v pracovní době se jmenují „nedostupné“.** API neřekne, jestli jde o rezervaci, přestávku nebo krátkou mezeru. Zamítnuto: „obsazeno“.
- **Bez určeného zdroje je úsek volný, když je volný aspoň jeden zdroj.** Konkrétního člověka si návštěvník vybere filtrem. Po první verzi může přibýt volba mezi sloučeným pohledem a pohledem po zdrojích. Zamítnuto pro první verzi: pruh pro každý zdroj.
- **U individuálních služeb se ukazuje, kdy mají zdroje volno, ne kdy jde začít konkrétní službu.** Stránka ukazuje zaplněnost a volnou kapacitu, výběr služby řeší rezervační formulář.
- **Volno zdroje je sjednocení volných začátků přes jeho veřejné služby.** Platí ve všech zobrazeních individuálních typů. `service` je u individuálních typů nepovinný a nedoplňuje se, s ním se volno počítá jen z té služby. Zamítnuto: volno z jedné služby (zkresluje), `BookingResource/GetAvailability` (hlásí volno i v obsazeném čase, `docs/SPEC.md` sekce 14).
- **Rezervace se smí číst jen kvůli obsazeným časům.** Čtou se z `Event/GetList`, bere se jen začátek, konec, zdroj a stav, údaje zákazníků se zahodí při načtení. Práva k zákazníkům a k zápisu projekt nežádá. Riziko úniku už vydaného tokenu ze serveru je přijaté, protože token může získat jen administrátor účtu, tedy majitel dat. Až API nabídne volné bloky bez osobních údajů, `Event_r` se zruší.
- **Přepínání týdnů přes parametr `week=RRRR-MM-DD`.** Odkaz na týden je stabilní, čtení se nemění. Neplatný tvar je „pohled není dostupný“, datum mimo období se posune na nejbližší nabízený týden. Zamítnuto: posun od dneška (`week=1`), přepínání jen skriptem.
- **`days` určuje, kam až jde přepnout.** Dny mimo období jsou označené a bez termínů. Zamítnuto: kalendář až do `MaxDays` bez ohledu na `days`.
- **Týden začíná pondělím v obou jazycích.** Zamítnuto: neděle v angličtině.
- **Angličtina se formátuje britsky (`en-GB`).** Zamítnuto: `en-US` s AM/PM.
- **Neznámý `lang` dá výchozí jazyk, jazyk prohlížeče se nebere v úvahu.** O jazyce rozhoduje merchant odkazem. Zamítnuto: hláška pro neznámý jazyk, výběr podle `Accept-Language`.
- **Veřejně navíc jdou názvy a Id veřejných zaměření a provozoven** a Id služeb a zdrojů, kvůli filtrům a odkazům na týdny. Zamítnuto: filtry bez Id.
- **Limit 120 požadavků za minutu na IP, pevně v kódu, pro všechny stránky kromě statických souborů.** Zamítnuto: limit jen pro stránku návštěvníka, konfigurovatelný limit.
- **`Stop` hook pustí tah, když build selže jen na souborech zamčených běžící aplikací.** Jiné chyby dál blokují.
- **Veřejné třídy a CSS proměnné mají prefix `av-`,** třídy podle BEM. Seznam je v `docs/vzhled.md`. Třídy stavů jsou vypsané v kódu, neodvozují se z názvů výčtů, aby přejmenování výčtu nezměnilo veřejné rozhraní. Zamítnuto: bez prefixu.
- **Stránka funguje bez JavaScriptu.** Filtry jsou formulář GET, týdny odkazy, skript jen odešle formulář po změně.

## 2026-11-07

- **Lekce bez služby v adrese ukazují lekce všech služeb provozovny.** Rozvrh s lekcemi jediné služby by návštěvníka zmátl. Zamítnuto: doplnit první veřejnou službu. Každý termín lekce službu má, OpenAPI ji uvádí jako povinnou.
- **Jak daleko dopředu jsou volné začátky individuálních služeb, určuje nastavení merchanta.** Stránka to nepřebíjí.
- **Stránka připojení ukazuje název účtu merchanta.** Správce tak hned pozná, ke kterému účtu se připojil. Stojí to právo `Merchant_r` navíc, z odpovědi se bere jen název. Token bez toho práva hlásí chybějící práva a vyzve k novému připojení. Zamítnuto: názvy veřejných provozoven, nové právo nepotřebují, ale účet podle nich pozná hůř.
- **Odpověď `Merchant/GetInfo` se nenahrává.** Nese IČO, kontakty a adresy. Smoke test ji čte naživo, testy bez sítě mají vlastní odpověď.

## 2026-11-06

- **Neveřejné objekty se nezobrazují vůbec, ani jejich termíny.** Termín u neveřejného zdroje nebo služby se vynechá celý. Zamítnuto: ukázat termín bez jména zdroje.
- **Podvržený odkaz na `/Auth/Callback` se neřeší.** Útočník by mohl správci poslat odkaz s tokenem ke svému účtu. Situace reálně nenastane, a kdyby ano, stránka ukáže cizí volno, ale k účtu správce se útočník nedostane. Zamítnuto: názvy zaměření a provozoven v ověření, upozornění na stránce s tokenem.
- **Token ukládá správce ručně.** Instance mu ho v průvodci ukáže a napíše, kam ho vložit. Zamítnuto: automatické uložení instancí, které by vyžadovalo zápis na disk a ochranu proti podstrčení jiného účtu.
- **Připojení vede průvodce na stránce `/Setup`,** krok za krokem, aby si instanci rozchodili i merchanti sami.
- **Token získává každá instance sama, s vlastním ClientId.** Každý merchant si registruje ClientId a návratovou adresu své instance. Zamítnuto: jeden společný ClientId a centrální pomocná stránka.
- **Volby zobrazení jsou v parametrech URL, v souboru jsou jen přístupové údaje a povolené domény.** Jedna instance tak umí víc pohledů. Generátor sestaví odkaz a kód iframe. Zamítnuto: všechno v souboru.
- **První verze pokrývá lekce a individuální zaměření.** Půjčovny a kurzy včetně výpisu kurzů vědomě ne, půjčovny se téměř nepoužívají. Filtry zaměření, provozovna, zdroj a služba, volitelně přepínatelné návštěvníkem. Jazyky čeština a angličtina.
- **Stránka vždy něco zobrazí.** Chybějící volba se nahradí první možností, nečeká se na návštěvníka. Zamítnuto: výběr služby před zobrazením termínů.
- **Cache:** volné termíny 60 sekund, zaměření, provozovny, zdroje a služby 1 hodinu, protože se téměř nemění.
- **Vývoj a testy běží proti produkčnímu API** (`https://api.reservanto.cz`). Testy z něj jen čtou.
- **Merchant si může dodat vlastní CSS,** aby stránka co nejvíc odpovídala jeho webu.
- **Docker zatím neřešíme.** Hostuje se na IIS.
- **Vývojová adresa je `https://localhost:5001`, návratová adresa `/Auth/Callback`.** Na ni si vývojář registruje svůj ClientId.
- **Jedna instance obsluhuje jediného merchanta.** Platí i pro hostování na společném serveru. Zamítnuto: společná instance pro více merchantů.
- **Konfigurace podle zvyklostí .NET 10**, tedy `appsettings.json` a user-secrets, ne `app.config`.
- **Projekt nemá databázi.** Data čte z Reservanto API, uložené údaje jsou v souboru na disku.
- **Merchanti si projekt mohou stáhnout a upravit.** Hostuje ho merchant sám, nebo Reservanto na vlastním serveru (IIS).
