import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { test } from 'node:test';
import { performance } from 'node:perf_hooks';

const readerRoot = new URL('../../src/SiloPlayer/Assets/Reader/', import.meta.url);
globalThis.NodeFilter = { SHOW_ELEMENT: 1, SHOW_TEXT: 4, SHOW_CDATA_SECTION: 8,
    FILTER_REJECT: 2, FILTER_SKIP: 3, FILTER_ACCEPT: 1 };
globalThis.HTMLElement = class extends EventTarget {
    attachShadow() { return {}; }
    hasAttribute() { return false; }
};
globalThis.customElements = { define() {} };
globalThis.document = {
    createTreeWalker(root) {
        let index = -1;
        return { nextNode: () => root.nodes[++index] ?? null };
    },
    createRange() {
        return {
            setStart(node, offset) { this.start = [node.index, offset]; },
            setEnd(node, offset) { this.end = [node.index, offset]; },
        };
    },
};
const { search } = await import(new URL('vendor/foliate-js/search.js', readerRoot));
const { View } = await import(new URL('vendor/foliate-js/view.js', readerRoot));
const options = { granularity: 'grapheme', sensitivity: 'variant' };
const chapter = texts => ({ body: { lang: 'en', nodes: texts.map((nodeValue, index) => ({ nodeValue, index })) } });
const makeView = sections => {
    const view = new View();
    view.book = { sections: sections.map(createDocument => ({ createDocument })) };
    view.outlines = [];
    view.getCFI = (index, range) => index + ':' + JSON.stringify(range.start);
    view.addAnnotation = item => view.outlines.push(item);
    view.deleteAnnotation = item => { view.outlines = view.outlines.filter(value => value !== item); };
    return view;
};
const collect = async iterator => {
    const results = [];
    for await (const result of iterator) results.push(result);
    return results;
};
const hits = results => results.flatMap(result => result.subitems ?? (result.cfi ? [result] : []));
const deferred = () => {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
};
const bridgeSource = await readFile(new URL('reader.mjs', readerRoot), 'utf8');
const bridge = (view, overrides = {}, isReady = true) => {
    const context = vm.createContext({ URLSearchParams, AbortController, SEARCH_RESULT_LIMIT: 1000, location: { search: '?generation=7&fileId=42' },
        document: {}, chrome: { webview: { postMessage() {} } }, fetch: async () => { throw new Error('missing'); }, ...overrides });
    context.window = context;
    vm.runInContext(bridgeSource.split(String.fromCharCode(10)).filter(line => !line.startsWith('import ')).join(String.fromCharCode(10))
        + ';globalThis.attachTestView = (value, isReady) => { view = value; book = { destroy() {} }; ready = isReady; };', context);
    context.attachTestView(view, isReady);
    return context.siloReader;
};

test('cross-node excerpts retain middle text and distinguish equal boundary nodes', () => {
    const result = [...search(['left ab', 'middle', 'cd right'], 'abmiddlecd', options)][0];
    assert.equal(result.excerpt.match, 'abmiddlecd');
    const repeated = [...search(['same', ' between ', 'same'], 'same between same', { sensitivity: 'base' })][0];
    assert.equal(repeated.excerpt.match, 'same between same');
    assert.deepEqual(repeated.range, { startIndex: 0, startOffset: 0, endIndex: 2, endOffset: 4 });
});

test('simple search retains matches ending at the final text node boundary', () => {
    for (const sensitivity of ['variant', 'accent']) {
        for (const [texts, query, range] of [
            [['The end'], 'end', { startIndex: 0, startOffset: 4, endIndex: 0, endOffset: 7 }],
            [['same', ' between ', 'same'], 'same between same', { startIndex: 0, startOffset: 0, endIndex: 2, endOffset: 4 }],
        ]) {
            const results = [...search(texts, query, { ...options, sensitivity })];
            assert.equal(results.length, 1);
            assert.deepEqual(results[0].range, range);
            assert.equal(results[0].excerpt.match, query);
            assert.equal(results[0].excerpt.post, '');
        }
    }
});

test('simple search preserves context and subsequent matches at mid-section node boundaries', () => {
    const results = [...search(['end', ' next end', ' tail'], 'end', options)];
    assert.deepEqual(results.map(result => result.range), [
        { startIndex: 0, startOffset: 0, endIndex: 1, endOffset: 0 },
        { startIndex: 1, startOffset: 6, endIndex: 2, endOffset: 0 },
    ]);
    assert.deepEqual(results.map(result => result.excerpt), [
        { pre: '', match: 'end', post: ' next end' },
        { pre: 'next ', match: 'end', post: ' tail' },
    ]);
});

test('windowed excerpts equal whole-node context including long whitespace runs', () => {
    for (const padding of ['abcd '.repeat(150), 'x' + ' '.repeat(4096), String.fromCharCode(9, 10, 32).repeat(2000) + 'tail']) {
        const result = [...search([padding + 'needle' + padding], 'needle', options)][0];
        const before = padding.replace(/\s+/g, ' ').trimStart();
        const after = padding.replace(/\s+/g, ' ').trimEnd();
        assert.deepEqual(result.excerpt, {
            pre: (before.length < 50 ? '' : '…') + before.slice(-50),
            match: 'needle', post: after.slice(0, 50) + (after.length < 50 ? '' : '…'),
        });
    }
});

test('long high-hit chapter stops at 1000, yields to timers and outlines no sentinel', async () => {
    const text = 'needle '.repeat(200000);
    const view = makeView([async () => chapter([text])]);
    let yielded = false;
    setTimeout(() => { yielded = true; }, 0);
    const start = performance.now();
    const results = await collect(view.search({ query: 'needle', matchCase: true, matchDiacritics: true }));
    assert.equal(hits(results).length, 1000);
    assert.equal(results.filter(result => result.truncated).length, 1);
    assert.equal(view.outlines.length, 1000);
    assert.equal(yielded, true);
    assert.ok(performance.now() - start < 5000, 'bounded search should not normalize a huge node per hit');
    for (const index of [0, 500, 999]) {
        const offset = index * 7;
        const before = text.slice(0, offset).replace(/\s+/g, ' ').trimStart();
        const after = text.slice(offset + 6).replace(/\s+/g, ' ').trimEnd();
        assert.deepEqual(hits(results)[index].excerpt, {
            pre: (before.length < 50 ? '' : '…') + before.slice(-50),
            match: 'needle', post: after.slice(0, 50) + (after.length < 50 ? '' : '…'),
        });
    }
});

test('cap is whole-book, exact cap is not truncated, later sections prove overflow', async () => {
    const exact = makeView([async () => chapter(['needle '.repeat(500)]), async () => chapter(['needle '.repeat(500)])]);
    const results = await collect(exact.search({ query: 'needle', limit: 1000 }));
    assert.equal(hits(results).length, 1000);
    assert.equal(results.some(result => result.truncated), false);
    const more = makeView([async () => chapter(['needle '.repeat(1000)]), async () => chapter(['needle'])]);
    const overflow = await collect(more.search({ query: 'needle', limit: 1000 }));
    assert.equal(hits(overflow).length, 1000);
    assert.equal(overflow.some(result => result.truncated), true);
    assert.equal(more.outlines.length, 1000);
});

test('abort during a high-hit section stops before outlines and completion', async () => {
    const view = makeView([async () => chapter(['needle '.repeat(5000)])]);
    const controller = new AbortController();
    setTimeout(() => controller.abort(), 0);
    const results = await collect(view.search({ query: 'needle', signal: controller.signal }));
    assert.equal(controller.signal.aborted, true);
    assert.equal(hits(results).length, 0);
    assert.equal(view.outlines.length, 0);
    assert.equal(results.includes('done'), false);
});

test('abort while loading a section cannot outline stale results', async () => {
    const load = deferred();
    const view = makeView([() => load.promise]);
    const controller = new AbortController();
    const pending = collect(view.search({ query: 'needle', signal: controller.signal }));
    await new Promise(resolve => setImmediate(resolve));
    controller.abort();
    load.resolve(chapter(['needle']));
    assert.deepEqual(await pending, []);
    assert.equal(view.outlines.length, 0);
});

test('section search obeys its limit and a pre-aborted search emits nothing', async () => {
    const view = makeView([async () => chapter(['needle '.repeat(50)])]);
    assert.equal(hits(await collect(view.search({ query: 'needle', index: 0, limit: 3 }))).length, 3);
    const controller = new AbortController();
    controller.abort();
    assert.deepEqual(await collect(view.search({ query: 'needle', signal: controller.signal })), []);
});

test('native bridge integrates bounded view results and truncation', async () => {
    const view = makeView([async () => chapter(['needle '.repeat(3000)])]);
    const outcome = await bridge(view).find('needle', 41);
    assert.equal(outcome.items.length, 1000);
    assert.equal(outcome.truncated, true);
    assert.equal(outcome.searchId, 41);
    assert.equal(outcome.hostGeneration, 7);
    assert.equal(outcome.fileId, 42);
});

test('superseded bridge search resolves null even if the old producer ignores abort', async () => {
    const old = deferred();
    const calls = [];
    const reader = bridge({ clearSearch() {}, async *search(options) {
        calls.push(options);
        if (options.query === 'old') await old.promise;
        yield { cfi: options.query, excerpt: options.query };
    } });
    const first = reader.find('old', 1);
    const latest = await reader.find('new', 2);
    old.resolve();
    assert.equal(await first, null);
    assert.equal(latest.items[0].cfi, 'new');
    assert.equal(calls[0].signal.aborted, true);
    assert.equal(calls[1].limit, 1000);
});

test('overlapping searches through the real view draw only the newest outlines', async () => {
    const old = deferred();
    const loading = deferred();
    let loads = 0;
    const view = makeView([() => {
        if (++loads === 1) { loading.resolve(); return old.promise; }
        return Promise.resolve(chapter(['new new ']));
    }]);
    const reader = bridge(view);
    const pending = reader.find('old', 1);
    await loading.promise;
    const newest = await reader.find('new', 2);
    old.resolve(chapter(['old old old ']));
    assert.equal(await pending, null);
    assert.equal(newest.items.length, 2);
    assert.equal(view.outlines.length, 2);
    assert.deepEqual(view.outlines.map(item => item.value), Array.from(newest.items, item => 'foliate-search:' + item.cfi));
});

test('a successful same-book file switch drops old CFIs and searches the new file', async () => {
    const old = deferred();
    const oldView = { clearSearch() {}, close() {}, remove() {}, async *search() {
        await old.promise;
        yield { cfi: 'old-file-cfi', excerpt: 'old' };
    } };
    const nextView = { clearSearch() {}, addEventListener() {}, async open() {}, async init() {}, async *search() {
        yield { cfi: 'new-file-cfi', excerpt: 'new' };
    } };
    const reader = bridge(oldView, {
        document: { body: { replaceChildren() {} }, createElement: () => nextView },
        File,
        fetch: async () => ({ ok: true, blob: async () => new Blob(['book']) }),
        makeBook: async () => ({ sections: [{ id: 'same-book-new-file' }] }),
    });
    const pending = reader.find('old', 11);
    assert.equal(await reader.open({ url: 'new-file.epub', format: 'epub' }), true);
    old.resolve();
    assert.equal(await pending, null);
    const outcome = await reader.find('new', 12);
    assert.equal(outcome.items[0].cfi, 'new-file-cfi');
    assert.equal(outcome.truncated, false);
});

test('clear, close and same-book file open retire pending bridge searches', async () => {
    for (const action of ['clearSearch', 'close', 'open']) {
        const load = deferred();
        let cleared = 0;
        const reader = bridge({ clearSearch() { cleared++; }, close() {}, remove() {}, async *search() {
            await load.promise;
            yield { cfi: 'old-file-cfi', excerpt: 'old file' };
        } });
        const pending = reader.find('needle', 5);
        if (action === 'open') await assert.rejects(reader.open({ url: 'new-file.epub' }));
        else reader[action]();
        load.resolve();
        assert.equal(await pending, null, action);
        assert.ok(cleared > 0, action);
    }
});

test('host scopes messages to search, open and file identities and localizes truncation', async () => {
    const host = await readFile(new URL('../../src/SiloPlayer/Views/EbookReaderPage.xaml.cs', import.meta.url), 'utf8');
    const locale = await readFile(new URL('../../src/SiloPlayer/Views/EbookReaderPage.Localization.cs', import.meta.url), 'utf8');
    assert.match(host, /GetProperty\("searchId"\).*_searchGeneration/);
    assert.match(host, /GetProperty\("fileId"\).*_fileId/);
    assert.match(host, /if \(_opening\) return;/);
    assert.match(host, /_leaving = true;\s*RetireSearch\(\);/);
    assert.match(host, /_opening = true;\s*RetireSearch\(\);/);
    assert.match(host, /searchGeneration != _searchGeneration/);
    assert.match(host, /SearchBook\(book, query, token\)/);
    assert.match(host, /outcome != null/);
    assert.match(locale, /T\("web.reader.search.truncated"/);
    assert.match(locale, /\["count"\] = count/);
});

test('native message identity predicate rejects stale query, file and open generations', async () => {
    const host = await readFile(new URL('../../src/SiloPlayer/Views/EbookReaderPage.xaml.cs', import.meta.url), 'utf8');
    const predicate = host.match(/if \((message.RootElement.GetProperty\("searchId"\)[\s\S]*?)\) return;/)?.[1];
    assert.ok(predicate, 'exercise the actual native identity expression');
    const reject = new Function('message', '_searchGeneration', '_openGeneration', '_fileId', 'return ' + predicate);
    const message = values => ({ RootElement: { GetProperty: name => ({ GetInt32: () => values[name] }) } });
    assert.equal(reject(message({ searchId: 8, hostGeneration: 3, fileId: 42 }), 8, 3, 42), false);
    assert.equal(reject(message({ searchId: 7, hostGeneration: 3, fileId: 42 }), 8, 3, 42), true);
    assert.equal(reject(message({ searchId: 8, hostGeneration: 2, fileId: 42 }), 8, 3, 42), true);
    assert.equal(reject(message({ searchId: 8, hostGeneration: 3, fileId: 41 }), 8, 3, 42), true);
});

test('native promise bridge suppresses retired outcomes and posts file-scoped results', async () => {
    const host = await readFile(new URL('../../src/SiloPlayer/Views/EbookReaderPage.xaml.cs', import.meta.url), 'utf8');
    const template = host.match(/ExecuteScriptAsync\(\$"(Promise.resolve\(window.siloReader\?\.find[^\n]+)"\);/)?.[1];
    assert.ok(template);
    const script = template.replace('{JsonSerializer.Serialize(query)}', JSON.stringify('needle'))
        .replaceAll('{searchGeneration}', '8').replaceAll('{openGeneration}', '3').replaceAll('{fileId}', '42')
        .replaceAll('{{', '{').replaceAll('}}', '}');
    const stale = await bridge({ clearSearch() {}, async *search() {
        yield { cfi: 'old-file', excerpt: 'old file' };
    } }, { location: { search: '?generation=2&fileId=41' } }).find('needle', 8);
    const predicate = host.match(/if \((message.RootElement.GetProperty\("searchId"\)[\s\S]*?)\) return;/)?.[1];
    assert.ok(predicate);
    const reject = new Function('message', '_searchGeneration', '_openGeneration', '_fileId', 'return ' + predicate);
    for (const outcome of [null, undefined,
        { items: [{ cfi: 'new-file' }], truncated: true, searchId: 8, hostGeneration: 3, fileId: 42 }, stale]) {
        const messages = [];
        await vm.runInNewContext(script, {
            window: { siloReader: outcome === undefined ? undefined : { async find(query, searchId) {
                assert.equal(query, 'needle'); assert.equal(searchId, 8); return outcome;
            } } },
            chrome: { webview: { postMessage: message => messages.push(JSON.parse(message)) } },
        });
        assert.equal(messages.length, outcome === null ? 0 : 1);
        if (outcome === undefined) assert.deepEqual(messages[0], {
            type: 'search-results', hostGeneration: 3, fileId: 42, searchId: 8, items: [], truncated: false, failed: true,
        });
        else if (outcome) assert.deepEqual(messages[0], JSON.parse(JSON.stringify({ type: 'search-results', ...outcome })));
        if (outcome === stale) {
            const message = { RootElement: { GetProperty: name => ({ GetInt32: () => messages[0][name] }) } };
            assert.equal(reject(message, 8, 3, 42), true, 'an old live page cannot claim the new file identity');
        }
    }
});


test('native excerpts join pre, match and post instead of exposing JSON', async () => {
    const reader = bridge({ clearSearch() {}, async *search() {
        yield { label: 'Chapter', subitems: [{ cfi: 'excerpt-cfi', excerpt: { pre: 'Before ', match: 'needle', post: ' after' } }] };
    } });
    const outcome = await reader.find('needle', 42);
    assert.equal(outcome.items[0].snippet, 'Before needle after');
});


test('native bridge reports a current search failure and missing renderer', async () => {
    const reader = bridge({ clearSearch() {}, async *search() { throw new Error('fixture failure'); } });
    const outcome = await reader.find('needle', 43);
    assert.equal(outcome.failed, true);
    assert.equal(outcome.searchId, 43);
    assert.equal(outcome.hostGeneration, 7);
    assert.equal(outcome.fileId, 42);
    const missing = await bridge({ clearSearch() {} }).find('needle', 44);
    assert.equal(missing.failed, true);
    assert.equal(missing.searchId, 44);
});

test('shared search is gated before dispatch and after the asynchronous clear', async () => {
    const host = await readFile(new URL('../../src/SiloPlayer/Views/EbookReaderPage.xaml.cs', import.meta.url), 'utf8');
    const runSearch = host.slice(host.indexOf('private async Task RunSearchAsync()'), host.indexOf('private static (List<BookSearchResult>'));
    assert.match(runSearch, /if \(_opening \|\| _leaving \|\| \(UsesSharedRenderer && !_rendererReady\)\) return;/);
    assert.match(runSearch, /await ClearRendererSearchAsync\(\);\s*if \([^\n]*\(UsesSharedRenderer && !_rendererReady\)/);
    const open = host.slice(host.indexOf('private async Task OpenVersionAsync('), host.indexOf('private async Task LoadMangaNavigationAsync()'));
    assert.ok(open.indexOf('_rendererReady = false;') < open.indexOf('ReaderWebView.Source ='));
    assert.match(open, /index.html\?generation=\{_openGeneration\}&fileId=\{_fileId\}/);
});

test('an unready shell fails search without touching its previous or opening view', async () => {
    let searched = false;
    const reader = bridge({ clearSearch() {}, async *search() { searched = true; } }, {}, false);
    const outcome = await reader.find('needle', 45);
    assert.equal(searched, false);
    assert.equal(outcome.failed, true);
    assert.equal(outcome.searchId, 45);
    assert.equal(outcome.hostGeneration, 7);
    assert.equal(outcome.fileId, 42);
});
