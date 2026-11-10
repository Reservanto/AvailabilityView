// Průvodce připojením a generátor: kopírování hodnot, odstranění tokenu z adresy a odeslání voleb generátoru po změně.
import { submitOnChange } from './autosubmit.js';

// Adresa návratu ze souhlasu nese token. Skript ji přepíše, aby nezůstala v adresním řádku a v historii karty.
if (document.querySelector('[data-av-forget-address]') !== null) {
    history.replaceState(null, '', location.pathname);
}

// Odkaz na souhlas otevře skript sám, bez skriptu na něj správce klikne.
document.querySelector<HTMLAnchorElement>('[data-av-follow]')?.click();

for (const button of document.querySelectorAll<HTMLButtonElement>('[data-av-copy]')) {
    const original = button.textContent ?? '';
    button.addEventListener('click', () => {
        const value = document.getElementById(button.dataset.avCopy ?? '')?.textContent ?? '';
        void navigator.clipboard.writeText(value).then(() => {
            button.textContent = button.dataset.avCopied ?? original;
            setTimeout(() => {
                button.textContent = original;
            }, 2000);
        });
    });
}

for (const form of document.querySelectorAll<HTMLFormElement>('[data-av-autosubmit]')) {
    submitOnChange(form);
}
