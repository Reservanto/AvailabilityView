// Odeslání formuláře hned po změně volby. Bez skriptu uživatel klikne na tlačítko.

// Chromium i Firefox mění hodnotu zavřeného seznamu šipkami a hned hlásí změnu. Odeslání by po první šipce načetlo
// stránku znovu a uživatel s klávesnicí by se k dalším možnostem nedostal. Změnu seznamu z klávesnice proto odešle
// až tlačítko. Zaškrtávací pole se mění jedním stiskem, odešle se hned. Číselné pole (počet dní v generátoru) se
// odešle až po opuštění pole, ať se mění myší, šipkami, nebo psaním.
const keys = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown']);

export function submitOnChange(form: HTMLFormElement): void {
    let fromKeyboard = false;
    form.addEventListener('keydown', (event) => {
        if (event.target instanceof HTMLSelectElement && (keys.has(event.key) || event.key.length === 1)) {
            fromKeyboard = true;
        }
    });
    form.addEventListener('pointerdown', () => {
        fromKeyboard = false;
    });
    form.addEventListener('change', (event) => {
        const target = event.target;
        const checkbox = target instanceof HTMLInputElement && target.type === 'checkbox';
        if ((target instanceof HTMLSelectElement && !fromKeyboard) || checkbox) {
            form.requestSubmit();
        }
    });
    form.addEventListener('focusout', (event) => {
        const target = event.target;
        if (target instanceof HTMLInputElement && target.type === 'number' && target.value !== target.defaultValue
            && form.checkValidity()) {
            form.requestSubmit();
        }
    });
}
