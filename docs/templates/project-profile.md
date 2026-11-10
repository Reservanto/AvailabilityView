# Profil projektu: <název>

Schváleno: <datum>
Pravidla, ke kterým se profil vztahuje: `docs/ai-working-agreement.md`.

## Oblasti a úroveň rizika

Hodnoť oblasti, ne celý projekt. Vysoká úroveň patří jen tam, kde chyba stojí opravdu hodně. U každé oblasti napiš i to, co vědomě neděláme, jinak se úsilí rozlije.

| Oblast | Úroveň | Proč | Co navíc / co vědomě neděláme |
|---|---|---|---|
| … | nízká / standardní / vysoká | … | … |

## Kontroly

**Deterministické** (každý tah přes `Stop` hook, později CI):
- typy a překlad,
- rychlé unit a property testy,
- skript konvencí,
- architektonické pravidlo: směr závislostí mezi vrstvami,
- detektor duplicit (jen varování).

**Nezávislé ověření:** <čím se ověří nejrizikovější část: skutečná knihovna, serializer, API nebo systém, se kterým se projekt integruje. Má běžet na stejné platformě jako produkce.>

**Evaluace milníku:**
- evaluátor v čistém kontextu: správnost a požadavky, nejvýš 2 kola,
- oponent Codex (read-only): jen oblasti s vysokou úrovní,
- review návrhu: jednou za milník, neblokující.

**Ověření UI:** <ukázka / screenshoty proti kritériím / E2E pro hlavní scénáře při uzavření milníku>

## Role modelů

| Role | Model | Effort |
|---|---|---|
| výchozí (implementace) | <aktuální Opus> | medium, u oblastí s vysokou úrovní high |
| evaluátor (čistý kontext) | <aktuální Opus> | high |
| oponent z jiné rodiny | Codex CLI, výchozí model z konfigurace | read-only |

Silnější model jen tehdy, když se evaluátor a oponent rozcházejí a nejde to rozhodnout testem.

## Autonomie

- AI commituje do větve milníku: ano.
- AI pushuje větve milníků: ano.
- Merge do `main`: AI udělá sama po zelených kontrolách, jakmile uživatel přijme výsledek milníku.
- Změny kontrol, hooků a AI nastavení: samostatný commit, ve zprávě vypsané.
- Ptá se navíc: <co je v tomto projektu citlivé, např. změna formátu dat>.

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
