# Profil projektu: AvailabilityView

Pravidla, ke kterým se profil vztahuje: `docs/ai-working-agreement.md`. Konvence kódu: `docs/conventions.md`.

Repozitář je veřejný (`https://github.com/Reservanto/AvailabilityView`), platí sekce Veřejný repozitář v dohodě o spolupráci.

## Oblasti a úroveň rizika

| Oblast | Úroveň | Proč | Co navíc / co vědomě neděláme |
|---|---|---|---|
| LongTimeToken a konfigurace | vysoká | Repozitář je veřejný a token dává přístup k účtu merchanta. S právem `Event_r` čte i rezervace s údaji zákazníků. Únik do gitu, logu nebo prohlížeče se nedá vzít zpět. | Kontrola tajných údajů v každém tahu, oponent Codex. Mimo připojovací stránku se token nikdy nedostane do prohlížeče. Žádáme jen práva ke čtení. Vlastní šifrování konfigurace neděláme, chrání ji oprávnění na disku. |
| Co stránka zveřejní | vysoká | Stránka je veřejná bez přihlášení, API ale vrací i soukromé zdroje, jejich e-maily a telefony. | Do prohlížeče jdou jen vyjmenovaná pole z veřejných objektů, hlídá to test. Oponent Codex. Z rezervací se bere jen začátek, konec, zdroj a stav, údaje zákazníků se zahodí při načtení a hlídá je test nad stránkou, logem, chybami a nahrávkami. Práva k zákazníkům nežádáme. |
| Připojení k Reservantu (`/Setup`, `/Auth/Callback`) | vysoká | Token přichází v adrese, takže hrozí únik do logů serveru. Stránka je dostupná bez přihlášení. | Test, že se token nedostane do logu. Oponent Codex. Připojení funguje jen na instanci bez tokenu. |
| Generátor odkazu | nízká | Pracuje jen s veřejnými daty a nic neukládá. | Rychlé testy hlavních scénářů. Přihlášení ani tajnou adresu neděláme. |
| Načítání dostupnosti z API | standardní | Špatná kapacita návštěvníka zmate, ale nic nerezervuje ani neúčtuje. Každý typ zaměření má v API jiné volání. | Testy nad nahrávkami API pro každý typ, okrajové případy časových pásem a přechodu na letní čas. Výpočty období, týdnů, měsíců a nejbližšího volna dostanou i oponenta Codex, oponent tam našel nejvíc chyb. |
| Zátěž API a cache | standardní | Veřejná stránka může rychle vyčerpat volání API a každé zobrazení by šlo až do Reservanta. | Krátká cache, omezení počtu požadavků. Sdílenou cache mezi instancemi neděláme. |
| Vložení přes iframe | standardní | Špatné bezpečnostní hlavičky znamenají, že se stránka na webu merchanta vůbec nezobrazí. | E2E test stránky vložené z cizí domény. |
| Vzhled zobrazení | nízká | Chyba je vidět hned a nemá dopad na data. | Rychlé testy hlavních scénářů, automatická kontrola přístupnosti. Testy pixelové shody neděláme. |
| Instalační návod pro merchanty | nízká | Text bez dopadu na data. | Návod jednou projdeme krok za krokem na čisté instalaci. |

## Kontroly

**Deterministické** (každý tah přes `Stop` hook, později CI):
- překlad s varováním jako chybou, styl kódu přes `.editorconfig`,
- rychlé testy bez sítě nad nahrávkami API,
- kontrola tajných údajů v repozitáři: tokeny, GUID ve tvaru ClientId nebo LongTimeToken, nahrávky bez osobních údajů a s neutrálními názvy,
- kontrola veřejného obsahu: místní cesty, cizí e-maily, soukromé výrazy z `.private/public-check.txt`, odkazy na milníky v kódu a odkazy na soubory, které v repozitáři nejsou,
- architektonické testy: směr závislostí mezi vrstvami,
- test veřejného výstupu: stránka neobsahuje nic mimo povolený seznam polí,
- detektor duplicit (jen varování).

**Nezávislé ověření:** skutečné produkční Reservanto API s testovacím účtem merchanta, pro každý podporovaný typ zaměření. Testy API jen čtou. Běží při uzavření milníku, ne v každém tahu. Testovací přístup nesmí skončit v repozitáři.

**Evaluace milníku:**
- evaluátor v čistém kontextu: správnost a požadavky, nejvýš 2 kola,
- oponent Codex (read-only): jen oblasti s vysokou úrovní a výpočty času. Když Codex v prostředí není, AI navrhne jeho použití, po odmítnutí evaluace proběhne bez něj a zpráva to uvede,
- review návrhu: jednou za milník, neblokující.

**Ověření UI:** screenshoty proti kritériím a E2E v Playwrightu pro hlavní scénáře při uzavření milníku, včetně vložení do iframe z jiné domény.

## Role modelů

| Role | Model | Effort |
|---|---|---|
| výchozí (implementace) | Claude Opus 5.5 | medium, u oblastí s vysokou úrovní high |
| evaluátor (čistý kontext) | Claude Opus 5.5 | high |
| oponent z jiné rodiny | Codex CLI, výchozí model z konfigurace | read-only |

Silnější model jen tehdy, když se evaluátor a oponent rozcházejí a nejde to rozhodnout testem.

## Autonomie

- AI commituje do větve milníku: ano.
- AI pushuje větve milníků do repozitáře: ano. Repozitář je veřejný, push je zveřejnění.
- Merge do `main`: AI udělá sama po zelených kontrolách, jakmile uživatel přijme výsledek milníku.
- Změny kontrol, hooků a AI nastavení: samostatný commit, ve zprávě vypsané.
- Ptá se navíc: rozšíření práv žádaných od merchanta, nové pole z rezervací, nové pole zobrazené veřejně, změna formátu konfigurace nebo parametrů URL (merchanti na nich budou stavět své kopie).

## Definice hotového (milník)

- Všechna akceptační kritéria jsou ✅ a každé má důkaz.
- Deterministické kontroly jsou zelené.
- Evaluace proběhla, nevyřešené nálezy jsou ve zprávě.
- `report.md` obsahuje odchylky od plánu a seznam změněných testů.
- Nová rozhodnutí jsou v `docs/decisions.md`.

## Komunikace

- Česky, stručně, rovnou k věci. Když AI nesouhlasí, řekne to.
- Otázky v textu odpovědi, ne přes výběrový widget.
- Když si AI není jistá, napíše to. Nehádá.
