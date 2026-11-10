# Vzhled stránky pro návštěvníky

Proměnné a třídy, na kterých merchant může stavět vlastní CSS (`AvailabilityView:CustomCssUrl`, sekce Vlastní CSS). Jsou to veřejné rozhraní: přejmenování nebo odstranění rozbije vzhled u merchantů, proto se mění jen jako změna veřejného rozhraní (`docs/project-profile.md`, sekce Autonomie).

Pravidla:

- Všechno má prefix `av-`, aby se to nepletlo s CSS webu merchanta. Třídy jsou pojmenované podle BEM: blok (`av-slot`), prvek (`av-slot__time`) a modifikátor (`av-slot--full`).
- Vzhled se nejlépe mění proměnnými na `.av-root`. Třídy jsou pro úpravy, které proměnné nepokryjí.
- Stránka nemá inline styly ani vložené `<style>`, CSP je nepovolí.
- Atributy `data-av-*` a `id` patří skriptu a popiskům formuláře, rozhraní nejsou.
- Seznam hlídá test `AppearanceContractTests`: chytí třídu nebo proměnnou ze stylů nebo z vykreslené stránky, která tady chybí, i položku, která už nikde není.

## Proměnné

Nastavené na `.av-root`.

| Proměnná | Výchozí | Význam |
|---|---|---|
| `--av-font` | systémové písmo | písmo stránky |
| `--av-font-size` | `15px` | základní velikost písma, ostatní velikosti jsou relativní |
| `--av-color-text` | `#1f2328` | text |
| `--av-color-muted` | `#5f6672` | doplňkový text: zdroj, popisky, dny mimo nabídku |
| `--av-color-bg` | `#ffffff` | pozadí stránky a karet termínů |
| `--av-color-surface` | `#f6f7f9` | pozadí dnů kalendáře, hlášek a čar osy |
| `--av-color-border` | `#dde1e6` | okraje |
| `--av-color-accent` | `#0b6bcb` | odkazy, dnešek, zvolené zobrazení, tlačítko filtrů, lekce na ose |
| `--av-color-full` | `#b42318` | plně obsazený termín |
| `--av-color-full-bg` | `#fde8e7` | pozadí plně obsazeného termínu a hlášky výpadku |
| `--av-color-free` | `#1a6b3c` | volno a obsazenost s volnými místy |
| `--av-color-free-bg` | `#e7f3ec` | pozadí volna |
| `--av-color-busy` | `#9a3412` | obsazeno na hodinové ose |
| `--av-color-busy-bg` | `#fde3d3` | pozadí obsazeného |
| `--av-color-unavailable-bg` | `#eceef1` | pozadí nedostupného na hodinové ose |
| `--av-hour-height` | `44px` | výška hodiny na hodinové ose |
| `--av-radius` | `6px` | zaoblení rohů |
| `--av-space-1` až `--av-space-5` | `4px` až `24px` | mezery |

Jednotlivé mezery: `--av-space-1`, `--av-space-2`, `--av-space-3`, `--av-space-4`, `--av-space-5`.

Technické proměnné nastavují třídy pozic hodinové osy, nepřepisujte je: `--av-at`, `--av-to`, `--av-lane`, `--av-lanes`.

## Třídy

### Stránka a hlášky

| Třída | Co to je |
|---|---|
| `av-page` | `body` stránky |
| `av-root` | obal obsahu, nese proměnné |
| `av-visually-hidden` | text jen pro čtečky obrazovky |
| `av-service` | název služby nad termíny, když ji merchant určil v adrese |
| `av-message` | hláška místo termínů (nic volného, nedostupný pohled, výpadek, příliš mnoho požadavků) |
| `av-message--error` | hláška výpadku a příliš mnoha požadavků |
| `av-message__action` | odkaz pod hláškou, u nepřipojené instance na průvodce |

### Přepínač zobrazení a filtry

| Třída | Co to je |
|---|---|
| `av-views` | přepínač zobrazení, jen při více hodnotách `view` |
| `av-views__list` | seznam tlačítek |
| `av-views__link` | tlačítko zobrazení, aktuální má `aria-current="page"` |
| `av-filters` | formulář filtrů z `pickers` |
| `av-filters__field` | jeden filtr s popiskem |
| `av-filters__label` | popisek filtru |
| `av-filters__select` | výběr možnosti |
| `av-filters__submit` | tlačítko „Zobrazit“ |

### Agenda

| Třída | Co to je |
|---|---|
| `av-agenda` | celá agenda |
| `av-agenda__day` | jeden den |
| `av-agenda__date` | nadpis dne |
| `av-agenda__date-today` | označení „dnes“ v nadpisu |
| `av-agenda__list` | termíny dne |
| `av-slot` | termín nebo volný úsek |
| `av-slot--full` | plně obsazený termín |
| `av-slot__time` | čas od–do |
| `av-slot__service` | název služby, obsahuje i zdroj |
| `av-slot__resource` | název zdroje |
| `av-slot__occupancy` | obsazenost |

### Navigace po týdnech a měsících

Stejné třídy má navigace kalendáře, hodinové osy i měsíce.

| Třída | Co to je |
|---|---|
| `av-nav` | navigace |
| `av-nav__title` | zobrazený týden nebo měsíc |
| `av-nav__link` | předchozí nebo další týden či měsíc |
| `av-nav__link--disabled` | šipka bez dalšího nabízeného týdne či měsíce |

### Kalendář se sloupci dnů

| Třída | Co to je |
|---|---|
| `av-calendar` | celý kalendář |
| `av-calendar__grid` | mřížka dnů |
| `av-calendar__day` | den |
| `av-calendar__day--today` | dnešek |
| `av-calendar__day--outside` | den mimo nabízené období |
| `av-calendar__date` | nadpis dne |
| `av-calendar__outside` | text „mimo nabídku“ |
| `av-calendar__slots` | termíny dne |
| `av-calendar__slot` | termín |
| `av-calendar__slot--full` | plně obsazený termín |
| `av-calendar__slot-time` | čas od–do |
| `av-calendar__slot-meta` | zdroj a obsazenost |

### Hodinová osa

| Třída | Co to je |
|---|---|
| `av-timegrid` | celá hodinová osa |
| `av-legend` | legenda stavů, jen u individuálních typů |
| `av-legend__item` | položka legendy |
| `av-legend__item--free` | volno |
| `av-legend__item--busy` | obsazeno |
| `av-legend__item--unavailable` | nedostupné |
| `av-timegrid__grid` | mřížka osy a dnů |
| `av-timegrid__axis` | sloupec s hodinami |
| `av-timegrid__hour` | popisek hodiny |
| `av-timegrid__day` | den |
| `av-timegrid__day--today` | dnešek |
| `av-timegrid__day--outside` | den mimo nabízené období |
| `av-timegrid__date` | nadpis dne |
| `av-timegrid__track` | časová dráha dne s úseky |
| `av-timegrid__size` | prázdný prvek, který roztáhne dráhu na celou délku osy |
| `av-block` | úsek nebo lekce na dráze |
| `av-block--free` | volno |
| `av-block--busy` | obsazeno |
| `av-block--unavailable` | nedostupné |
| `av-block--lesson` | lekce |
| `av-block--lesson-full` | plně obsazená lekce |
| `av-block__text` | text v úseku, na úzké šířce skrytý |
| `av-timegrid__list` | úseky dne textem, vidět jen na úzké šířce |
| `av-timegrid__item` | úsek v seznamu |
| `av-timegrid__item--free` | volno v seznamu |
| `av-timegrid__item--lesson` | lekce v seznamu |
| `av-timegrid__item--lesson-full` | plně obsazená lekce v seznamu |

Technické třídy pozic, nepřepisujte je: `av-at-{N}` a `av-to-{N}` (začátek a konec úseku v řádcích po pěti minutách od začátku osy), `av-lane-{N}` (pruh souběžné lekce, od 0), `av-lanes-{N}` (počet pruhů dne).

### Měsíční přehled

Stav dne nese třída a text pro čtečky. Den s volnem je odkaz na svůj týden, když `view` obsahuje kalendář nebo hodinovou osu.

| Třída | Co to je |
|---|---|
| `av-month` | celý měsíc |
| `av-month__grid` | tabulka dnů od pondělí |
| `av-month__weekday` | záhlaví dne týdne |
| `av-month__day` | den |
| `av-month__day--free` | den s volnem |
| `av-month__day--full` | plný den: všechny lekce plné, nebo zdroje pracují bez volna |
| `av-month__day--closed` | den bez lekcí, nebo kdy žádný zdroj nepracuje |
| `av-month__day--outside` | den mimo nabízené období |
| `av-month__day--empty` | prázdná buňka za dnem jiného měsíce |
| `av-month__day--today` | dnešek |
| `av-month__link` | odkaz dne na jeho týden |
| `av-month__number` | číslo dne |
| `av-month__legend` | vysvětlivky stavů |
| `av-month__key` | jedna vysvětlivka |
| `av-month__key--free` | vysvětlivka volna |
| `av-month__key--full` | vysvětlivka plného dne |
| `av-month__key--closed` | vysvětlivka zavřeného dne |

### Nejbližší volný termín

| Třída | Co to je |
|---|---|
| `av-next` | věta s nejbližším volným termínem |
| `av-next--none` | věta, že v období nic volného není |

## Úzká šířka

Pod 720 px jdou dny kalendáře pod sebe a hodinová osa ukáže každý den jako vodorovný pruh se seznamem úseků pod ním. Pod 480 px se v agendě obsazenost přesune pod název. Stránka nemá vodorovný posuvník ani na 320 px.

## Vlastní CSS

Klíč `AvailabilityView:CustomCssUrl` v `appsettings.Production.json` načte styl merchanta za styly aplikace, takže pravidla se stejnou specifičností vyhrají. Příklad, který změní barvu zvýraznění:

```css
.av-root {
    --av-color-accent: #5b21b6;
}
```

- Hodnota je absolutní adresa `https://…`, třeba `https://www.vas-web.cz/styly/volno.css`, nebo cesta od kořene instance, třeba `/custom/web.css`. Pod IIS aplikací se cesta bere od její cesty, `/custom/web.css` je pak `https://server/volno/custom/web.css`.
- Jiné hodnoty, třeba `http://…`, adresa začínající `//`, cesta bez úvodního lomítka nebo hodnota s mezerou, nedovolí start aplikace. Změna platí po restartu.
- Styl se načte jen na stránce návštěvníka, i s hláškami, stránkou „příliš mnoho požadavků“ a ve vloženém iframe. Průvodce a generátor ho nemají, náhled v generátoru ano.
- CSP stránky povolí styly a písma (`@font-face`) jen z instance a z originu adresy stylu, `@import` jde jen ze stejných dvou míst. Obrázky jdou jen z instance. Písma jinde, třeba z Google Fonts, se nenačtou.
- Písmo z webu merchanta prohlížeč použije, jen když ho web posílá s hlavičkou `Access-Control-Allow-Origin` (`*` nebo adresa instance). Bez ní zůstane záložní písmo z `--av-font`.
- Nejjednodušší je mít soubor na webu merchanta. Soubor v instanci dejte do `wwwroot/custom/`. Publikace, která maže soubory navíc, ho smaže, proto si ho držte i jinde. Návod na IIS to popíše u aktualizace.

## Vložení do webu

Kód z generátoru je iframe se stránkou a skript, který iframe nastaví výšku podle obsahu. Adresa skriptu a formát zprávy jsou veřejné rozhraní stejně jako třídy.

```html
<iframe src="https://instance/?segment=6&amp;location=2023" title="Volné termíny" width="100%" height="600" style="border: 0;"></iframe>
<script src="https://instance/embed.js" async></script>
```

- Skript je na adrese `embed.js` v kořeni instance, pod IIS aplikací v její cestě, třeba `https://server/volno/embed.js`.
- Vložená stránka posílá rodiči po načtení a při každé změně výšky zprávu `postMessage` s daty `{ "type": "av:height", "height": 812 }`. Výška je v CSS pixelech, celé číslo.
- `embed.js` po načtení pošle každému iframe instance zprávu `{ "type": "av:height-request" }` a stránka v něm odpoví zprávou o výšce. Skript se tak může načíst i později než stránka v iframe, třeba odložený nástrojem webu.
- `embed.js` zprávu přijme jen z originu instance a jen od iframe, který ji poslal a jehož adresa začíná adresou instance. Výšku nastaví do `style.height` tohoto iframe, jiné nemění. Pro víc pohledů na jedné stránce stačí jeden skript.
- Bez skriptu má iframe pevnou výšku z atributu `height` a stránka v něm funguje, jen může mít vlastní posuvník.
- Web s vlastní CSP musí povolit adresu instance ve `frame-src` a ve `script-src`. Bez `script-src` zůstane iframe na pevné výšce.
- Stránku jde vložit jen z domén v `AvailabilityView:AllowedFrameOrigins` (`docs/SPEC.md` sekce 5).
