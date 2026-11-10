# Evaluace milníku

Nejvýš 2 kola. V kole 1 běží souběžně evaluátor v čistém kontextu (subagent) a u oblastí s vysokou úrovní Codex, oba se stejným zadáním. V kole 2 stačí Codex nad opravami z kola 1. Během evaluace kód neupravuj, oba evaluátoři ho čtou.

## Spuštění Codexu

Zadání ulož do souboru mimo repo a předej ho přes stdin. Bez přesměrování stdin Codex visí na jeho čtení, zadání v argumentu proto spouštěj s `< /dev/null`. Spouštěj na pozadí.

```bash
codex exec -s read-only -C . -o "<scratchpad>/codex.md" - < "<scratchpad>/zadani.md" > "<scratchpad>/codex.log" 2>&1
```

Když Codex odmítne model, nejdřív aktualizuj CLI.

## Zadání: kolo 1

```text
You are an independent, skeptical reviewer of milestone <Mx> in the repository at <path>. You did not write this code. Do not modify any files in the repository.

Context:
- <Co projekt dělá a co tento milník přidal, 2–3 věty.>
- Requirements and acceptance criteria: docs/milestones/<Mx>/plan.md. Product spec: docs/SPEC.md. <Další fakta.>
- Code under review: <cesty, nebo git diff main...HEAD>. Tests: <cesty>.
- Commands: <rychlé kontroly>, <všechny kontroly>.

Your task: find concrete correctness defects and unmet requirements. For each candidate, find a specific input that leads to a wrong result, and confirm it by reading the code or by running it (write throwaway scripts outside the repository). Focus on:
1. <Riziková místa tohoto milníku.>
2. Places where the tests or checks would not catch a real regression (a test that cannot fail, a check that careless code gets past).
3. Acceptance criteria in plan.md that are not actually met or not actually evidenced.

Report ONLY blocking findings: correctness errors and unmet requirements. Do not report style, naming, or "could be nicer" suggestions. Do not report theoretical bypasses that require deliberate sabotage.

For each finding give: title; file and line; concrete input -> wrong result and how you confirmed it; why it matters. If you find nothing blocking, say so and list what you checked.
```

## Zadání: kolo 2

```text
You are an independent, skeptical reviewer doing the SECOND (final) evaluation round of milestone <Mx> in <path>. Do not modify any files in the repository.

Round 1 found these issues, fixed after commit <hash> (see `git diff <hash> HEAD`):
<seznam nálezů a oprav>

Your task:
A. Verify each fix is correct and complete. Try to find a concrete input for which the fixed behaviour is still wrong.
B. Find regressions introduced by these changes.

Known and accepted (do not report): <přijaté limity>

Report ONLY blocking findings, in the same format: title; file and line; concrete input -> wrong result and how you confirmed it; why it matters. If nothing blocking remains, say so and list what you checked.
```

## Po evaluaci

- Každou opravu doplň testem, který na původním kódu selže.
- Nálezy zapiš do `report.md` do tabulky s výsledkem.
- Když oprava otevírá další chyby stejné třídy, zastav se a dej to uživateli k rozhodnutí.
- Review návrhu je samostatný neblokující běh evaluátora: hlásí jen nálezy, které by zlevnily nebo zjednodušily další změny.
