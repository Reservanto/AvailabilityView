// Stránka návštěvníka: filtry se odešlou hned po změně. Ve vložené stránce posílá rodiči výšku
// obsahu pro embed.js.
import { submitOnChange } from './autosubmit.js';

for (const form of document.querySelectorAll<HTMLFormElement>('[data-av-filters]')) {
    submitOnChange(form);
}

// Výška není tajná a origin rodiče stránka nezná (Referer se neposílá), proto zpráva jde komukoli.
if (window.parent !== window) {
    let sent = 0;
    const send = (): void => {
        const height = Math.ceil(document.documentElement.getBoundingClientRect().height);
        if (height !== sent) {
            sent = height;
            window.parent.postMessage({ type: 'av:height', height }, '*');
        }
    };
    new ResizeObserver(send).observe(document.documentElement);
    send();

    // embed.js se může načíst až po první zprávě (async, odložené skripty webu). Po startu proto o výšku požádá
    // a stránka ji pošle znovu, i když se nezměnila.
    window.addEventListener('message', (event) => {
        const data: unknown = event.data;
        if (event.source === window.parent && typeof data === 'object' && data !== null
            && (data as { type?: unknown }).type === 'av:height-request') {
            sent = 0;
            send();
        }
    });
}
