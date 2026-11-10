// Pozná, že rychlé kontroly selhaly jen proto, že běžící aplikace zamyká přeložené soubory.
// Aplikaci spouští uživatel a AI ji zastavit nesmí, takže by hook tah nepustil nikdy.
// Kódy MSBuildu jsou stejné v každém jazyce SDK, texty ne:
//   MSB3026 = opakování kopírování (upozornění), MSB3027 = vzdáno po opakováních, MSB3021 = soubor nejde zkopírovat.
const lockCodes = new Set(["MSB3021", "MSB3026", "MSB3027"]);

/**
 * true = selhal jen překlad a každá chyba v něm je zamčený soubor. Jiná chyba, nebo chyba v jiném kroku = false.
 * @param {string} output stdout a stderr z eng/check.ps1
 */
export function isLockedBuildOnly(output) {
	// eng/check.ps1 vypíše "CHYBA: <krok> (kod N)" a skončí na prvním neúspěšném kroku
	if (!/^CHYBA: Preklad \(/m.test(output)) {
		return false;
	}
	const codes = [...output.matchAll(/\berror ([A-Z]+[0-9]+)\s*:/g)].map(match => match[1]);
	return codes.length > 0 && codes.every(code => lockCodes.has(code));
}
