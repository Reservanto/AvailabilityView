// node --test .claude/hooks/locked-build.test.mjs (spouští eng/check.ps1). Ukázky jsou zkrácený skutečný výstup z 2026-11-08,
// kdy běžící AvailabilityView.Web zamkl své knihovny. Cesty jsou zkrácené.
import { test } from "node:test";
import assert from "node:assert/strict";
import { isLockedBuildOnly } from "./locked-build.mjs";

const targets = "C:\\Program Files\\dotnet\\sdk\\10.0.403\\Microsoft.Common.CurrentVersion.targets(5096,5)";
const retry = `${targets}: warning MSB3026: Soubor C:\\repo\\src\\AvailabilityView.Application\\bin\\Debug\\net10.0\\AvailabilityView.Application.dll nelze zkopírovat do umístění bin\\Debug\\net10.0\\AvailabilityView.Application.dll. Za 1000 ms bude zahájeno opakování 1. The process cannot access the file because it is being used by another process.`;
const gaveUp = `${targets}: error MSB3027: Soubor C:\\repo\\src\\AvailabilityView.Application\\bin\\Debug\\net10.0\\AvailabilityView.Application.dll nelze zkopírovat do umístění bin\\Debug\\net10.0\\AvailabilityView.Application.dll. Byl překročen počet opakování 10. Nezdařilo se. Soubor uzamkl(a): AvailabilityView.Web (50572). [C:\\repo\\src\\AvailabilityView.Web\\AvailabilityView.Web.csproj]`;
const cannotCopy = `${targets}: error MSB3021: Soubor C:\\repo\\src\\AvailabilityView.Application\\bin\\Debug\\net10.0\\AvailabilityView.Application.dll nelze zkopírovat do umístění bin\\Debug\\net10.0\\AvailabilityView.Application.dll. The process cannot access the file because it is being used by another process. [C:\\repo\\src\\AvailabilityView.Web\\AvailabilityView.Web.csproj]`;
const compileError = "C:\\repo\\src\\AvailabilityView.Web\\Program.cs(12,5): error CS1002: Očekává se ;. [C:\\repo\\src\\AvailabilityView.Web\\AvailabilityView.Web.csproj]";

const run = (step, ...lines) => ["== Preklad", ...lines, "Sestavení SE NEZDAŘILO.", `CHYBA: ${step} (kod 1)`].join("\r\n");

test("zamčené soubory běžící aplikace tah pustí", () => {
	assert.equal(isLockedBuildOnly(run("Preklad", retry, gaveUp, cannotCopy, "    10 upozornění", "    Počet chyb: 2")), true);
});

test("chyba překladu kódu tah nepustí, ani spolu se zamčenými soubory", () => {
	assert.equal(isLockedBuildOnly(run("Preklad", compileError)), false);
	assert.equal(isLockedBuildOnly(run("Preklad", retry, gaveUp, compileError)), false);
});

test("selhání v jiném kroku tah nepustí", () => {
	assert.equal(isLockedBuildOnly(run("Testy", gaveUp)), false);
	assert.equal(isLockedBuildOnly(["== Preklad", "== Testy", "  failed Neco_Selhalo", "CHYBA: Testy (kod 2)"].join("\n")), false);
});

test("překlad bez kódu chyby tah nepustí", () => {
	assert.equal(isLockedBuildOnly(run("Preklad", retry)), false);
	assert.equal(isLockedBuildOnly(run("Preklad")), false);
});
