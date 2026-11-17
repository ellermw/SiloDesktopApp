(() => {
    const hostGeneration = Number(new URLSearchParams(location.search).get('generation') ?? 0);
    const report = () => {
        if (!globalThis.siloReader) globalThis.chrome?.webview?.postMessage(JSON.stringify({type:'reader-load-error',hostGeneration}));
    };
    globalThis.addEventListener('error', event => {
        if (event.target?.tagName === 'SCRIPT' || event.target === globalThis) report();
    }, true);
    globalThis.addEventListener('unhandledrejection', report);
})();
