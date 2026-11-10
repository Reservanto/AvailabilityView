// Stop hook: před koncem tahu spustí rychlé kontroly. Příkaz je v .claude/checks.json ("fast").
// - Dokud je "fast" null (projekt ještě nemá kostru), hook nic nekontroluje.
// - Když se pracovní strom od posledního zeleného běhu nezměnil, nic nespouští.
// - Když je v progress.md některého milníku řádek "BLOCKED:", pustí (AI čeká na uživatele).
// - Když selže jen překlad na souborech zamčených běžící aplikací, pustí s upozorněním (locked-build.mjs).
// - Při červené vrátí exit 2 a výstup kontrol, takže tah nejde ukončit.
// Strop opakovaných blokací hlídá Claude Code sám (CLAUDE_CODE_STOP_HOOK_BLOCK_CAP).
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { isLockedBuildOnly } from "./locked-build.mjs";

// kořen repa = dvě úrovně nad .claude/hooks/
const root = dirname(dirname(dirname(fileURLToPath(import.meta.url))));
const stampFile = join(root, ".claude", ".last-green");

const command = fastCommand();
if (command === null || hasBlockedLine()) {
	process.exit(0);
}

const state = workingTreeState();
if (state !== null && existsSync(stampFile) && readFileSync(stampFile, "utf8") === state) {
	process.exit(0);
}

const result = spawnSync(command, { cwd: root, encoding: "utf8", shell: true, timeout: 300_000 });
if (result.status === 0) {
	if (state !== null) {
		writeFileSync(stampFile, state);
	}
	process.exit(0);
}

const text = (result.stdout || "") + (result.stderr || "");
if (isLockedBuildOnly(text)) {
	// Otisk se nezapíše: kód zkontrolovaný není a příští tah kontroly spustí znovu.
	process.stdout.write(JSON.stringify({
		systemMessage: "Rychlé kontroly neproběhly: běžící aplikace zamyká přeložené soubory. Zastav ji a kontroly se spustí v dalším tahu.",
	}));
	process.exit(0);
}

const output = text.split(/\r?\n/).filter(l => l.trim() !== "");
process.stderr.write(
	"Rychlé kontroly neprošly (" + command + "). Oprav je, než tah ukončíš.\n"
	+ "Když nejde pokračovat bez uživatele, zapiš do progress.md milníku řádek \"BLOCKED: <otázka>\".\n\n"
	+ output.slice(-60).join("\n") + "\n"
);
process.exit(2);

function fastCommand() {
	const file = join(root, ".claude", "checks.json");
	return existsSync(file) ? JSON.parse(readFileSync(file, "utf8")).fast ?? null : null;
}

function hasBlockedLine() {
	const dir = join(root, "docs", "milestones");
	if (!existsSync(dir)) {
		return false;
	}
	// jen rozpracované milníky (bez report.md), aby zapomenutý řádek v uzavřeném milníku hook trvale nevypnul
	return readdirSync(dir).some(milestone => {
		const progress = join(dir, milestone, "progress.md");
		return !existsSync(join(dir, milestone, "report.md"))
			&& existsSync(progress) && /^BLOCKED:/m.test(readFileSync(progress, "utf8"));
	});
}

/**
 * Otisk stavu: HEAD + seznam změněných a nesledovaných souborů + jejich obsah. null = git nedostupný.
 */
function workingTreeState() {
	const git = args => spawnSync("git", args, { cwd: root, encoding: "utf8" });
	const head = git(["rev-parse", "HEAD"]);
	// -z: cesty bez uvozovek a escapování (jinak by se soubory s diakritikou nehashovaly)
	const status = git(["status", "--porcelain=v1", "-z", "-uall"]);
	if (head.status !== 0 || status.status !== 0) {
		return null;
	}

	const hash = createHash("sha256").update(head.stdout).update(status.stdout);
	const entries = status.stdout.split("\0").filter(Boolean);
	for (let i = 0; i < entries.length; i++) {
		const entry = entries[i];
		// u přejmenování následuje původní cesta jako samostatná položka; hashujeme novou
		if (entry[0] === "R" || entry[0] === "C") {
			i++;
		}
		const path = entry.slice(3);
		const full = join(root, path);
		if (existsSync(full)) {
			try {
				hash.update(path).update(readFileSync(full));
			}
			catch {
				// složka nebo nečitelný soubor: stačí cesta
				hash.update(path);
			}
		}
	}
	return hash.digest("hex");
}
