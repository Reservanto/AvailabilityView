// Skript pro web merchanta: nastaví iframe se stránkou návštěvníka výšku podle obsahu.
// Vkládá se jako <script src="https://instance/embed.js" async>. Adresa a formát zprávy jsou veřejné rozhraní
// (docs/vzhled.md). Běží na cizí stránce: nic nedává do globálního prostoru a nevěří žádné zprávě, kterou neposlal
// iframe této instance.
(() => {
    const script = document.currentScript;
    if (!(script instanceof HTMLScriptElement)) {
        return;
    }

    // Adresa instance i s cestou aplikace pod IIS, končí lomítkem.
    const instance = new URL('.', script.src);
    const maxHeight = 100000;

    const isInstanceFrame = (frame: HTMLIFrameElement): boolean => frame.src.startsWith(instance.href);

    window.addEventListener('message', (event) => {
        if (event.origin !== instance.origin) {
            return;
        }

        const data: unknown = event.data;
        if (typeof data !== 'object' || data === null) {
            return;
        }

        const message = data as { type?: unknown; height?: unknown };
        const height = message.height;
        if (message.type !== 'av:height' || typeof height !== 'number' || !Number.isFinite(height) || height <= 0 || height > maxHeight) {
            return;
        }

        for (const frame of document.querySelectorAll('iframe')) {
            if (frame.contentWindow === event.source && isInstanceFrame(frame)) {
                frame.style.height = `${Math.ceil(height)}px`;
            }
        }
    });

    // Rám mohl výšku poslat dřív, než se skript načetl. Rám, který se ještě načítá, žádost nedostane (jiný origin)
    // a výšku pošle sám po načtení.
    for (const frame of document.querySelectorAll('iframe')) {
        if (isInstanceFrame(frame)) {
            frame.contentWindow?.postMessage({ type: 'av:height-request' }, instance.origin);
        }
    }
})();
