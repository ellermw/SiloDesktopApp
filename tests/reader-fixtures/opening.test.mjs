import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';
import { makeComicBook } from '../../src/SiloPlayer/Assets/Reader/vendor/foliate-js/comic-book.js';
import { parseLocation, progressFromRelocate } from '../../src/SiloPlayer/Assets/Reader/locations.mjs';

const source = readFileSync(new URL('../../src/SiloPlayer/Assets/Reader/reader.mjs', import.meta.url), 'utf8')
    .split('\n').filter(line => !line.startsWith('import ')).join('\n');

function openingHarness(sectionIds = Array.from({ length: 6 }, (_, index) => 'section-' + index)) {
    const messages = [];
    const listeners = new Map();
    const fractionRestores = [];
    const directRestores = [];
    const book = {
        sections: sectionIds.map(id => ({ id })),
        toc: [], destroy() {},
    };
    let currentIndex = 0;
    const relocate = index => {
        currentIndex = index;
        listeners.get('relocate')?.({ detail: {
            section: { current: index }, location: { current: index, total: sectionIds.length },
        } });
    };
    const view = {
        addEventListener: (name, handler) => listeners.set(name, handler),
        open: async () => {}, close() {}, remove() {},
        renderer: {
            setAttribute() {}, removeAttribute() {}, getContents: () => [],
            goTo: async target => { directRestores.push(target); relocate(target.index); },
        },
        resolveNavigation: async target => typeof target?.fraction === 'number'
            ? { index: Math.min(5, Math.floor(target.fraction * 6)), anchor: 0 } : null,
        goToFraction: async fraction => {
            fractionRestores.push(fraction);
            relocate(Math.min(5, Math.floor(fraction * 6)));
        },
        init: async () => relocate(0),
        next: async () => relocate(Math.min(sectionIds.length - 1, currentIndex + 1)),
        prev: async () => relocate(Math.max(0, currentIndex - 1)),
    };
    const context = vm.createContext({
        URLSearchParams, AbortController, parseLocation, progressFromRelocate,
        location: { search: '?generation=11&fileId=42' },
        File: class { constructor(parts, name) { this.name = name; } },
        fetch: async () => ({ ok: true, blob: async () => ({}) }),
        makeBook: async () => book,
        document: { body: { style: {}, replaceChildren() {} }, createElement: () => view },
        chrome: { webview: { postMessage: message => messages.push(JSON.parse(message)) } },
    });
    context.window = context;
    vm.runInContext(source, context);
    return { reader: context.siloReader, messages, fractionRestores, directRestores, relocate };
}

test('an unresolved saved location stays recoverable without writing a replacement position', async () => {
    const { reader, messages } = openingHarness();
    const opened = await reader.open({ url: 'fixture.epub', format: 'epub', location: 'unrecognized saved location' });
    assert.equal(opened, false);
    assert.equal(reader.snapshot(), null);
    assert.equal(messages.some(message => message.type === 'reader-error'), true);
    assert.equal(messages.some(message => message.type === 'reader-open-error' || message.type === 'reader-ready' || message.type === 'relocate'), false);
    assert.equal(await reader.chapter(1), true);
    assert.equal(reader.snapshot()?.index, 1);
    assert.equal(messages.some(message => message.type === 'reader-ready'), true);
});

for (const direction of ['next', 'prev']) {
    test(direction + ' explicitly recovers an unresolved restore and resumes progress messages', async () => {
        const { reader, messages, relocate } = openingHarness();
        assert.equal(await reader.open({ url: 'fixture.epub', format: 'epub', location: 'unrecognized saved location' }), false);
        // Layout/appearance relocations alone must not overwrite saved progress.
        relocate(2);
        assert.equal(reader.snapshot(), null);
        assert.equal(messages.some(message => message.type === 'reader-ready' || message.type === 'relocate'), false);
        await reader[direction]();
        const expected = direction === 'next' ? 3 : 1;
        assert.equal(reader.snapshot()?.index, expected);
        assert.equal(messages.at(-2).type, 'reader-ready');
        assert.equal(messages.at(-1).type, 'relocate');
        assert.equal(messages.at(-1).index, expected);
        await reader.next();
        assert.equal(messages.at(-1).index, expected + 1);
        assert.equal(messages.filter(message => message.type === 'reader-ready').length, 1);
    });
}

for (const format of ['cbz', 'cbr']) {
    test(format + ' restores native comic progress and bookmarks by base Windows paths, excluding new formats', async () => {
        const filenames = ['Chapter 1/001.jpg', 'Chapter 10/001.jpg', 'Chapter 1/000.avif', 'Chapter 1/002.jxl', 'Chapter 1/003.svg'];
        const comic = await makeComicBook({ entries: filenames.map(filename => ({ filename })), getSize: () => 1 }, { name: 'fixture.cbz' });
        const ids = comic.sections.map(section => section.id);
        // The native extractor supplies the base extension set in backslash order.
        const chapters = ['Chapter 10/001.jpg', 'Chapter 1/001.jpg', 'Chapter 1/003.svg'];
        const { reader, directRestores } = openingHarness(ids);
        assert.equal(await reader.open({ url: 'fixture.' + format, format, chapters, location: 'chapter:0;fraction:0.25' }), true);
        assert.equal(directRestores[0].index, ids.indexOf(chapters[0]));
        assert.equal(directRestores[0].anchor, .25);
        for (let index = 1; index < chapters.length; index++) {
            assert.equal(await reader.go(`chapter:${index};fraction:0.5`), true);
            assert.equal(directRestores.at(-1).index, ids.indexOf(chapters[index]));
        }
        assert.equal(await reader.go('chapter:3;fraction:0'), false);
    });
}

for (const format of ['fb2', 'fbz']) {
    test(format + ' restores legacy desktop chapters by saved overall progress, not foliate section number', async () => {
        const { reader, fractionRestores, directRestores } = openingHarness();
        const opened = await reader.open({
            url: 'fixture.' + format, format, location: 'chapter:2;fraction:0.25', progress: .75,
            chapters: ['fb2-chapter-0001.html', 'fb2-chapter-0002.html', 'fb2-chapter-0003.html'],
        });
        assert.equal(opened, true);
        assert.deepEqual(fractionRestores, [.75]);
        assert.deepEqual(directRestores, []);
        assert.equal(reader.snapshot()?.index, 4);
    });

    test(format + ' refuses legacy bookmark and highlight navigation instead of guessing a section', async () => {
        const { reader, messages, fractionRestores, directRestores } = openingHarness();
        assert.equal(await reader.open({
            url: 'fixture.' + format, format, location: 'chapter:2;fraction:0.25', progress: .75,
            chapters: ['fb2-chapter-0001.html', 'fb2-chapter-0002.html', 'fb2-chapter-0003.html'],
            annotations: [
                { kind: 'bookmark', location: 'chapter:1;fraction:0.5' },
                { kind: 'highlight', location: 'chapter:1;fraction:0.5', selectedText: 'Old highlight' },
            ],
        }), true);
        const saved = reader.snapshot();
        const relocated = messages.filter(message => message.type === 'relocate').length;
        assert.equal(await reader.go('chapter:1;fraction:0.5'), false);
        assert.equal(reader.snapshot(), saved);
        assert.deepEqual(directRestores, []);
        assert.deepEqual(fractionRestores, [.75]);
        assert.equal(messages.filter(message => message.type === 'relocate').length, relocated);
        assert.equal(messages.at(-1).type, 'reader-error');
        assert.match(messages.at(-1).message, /unavailable/);
    });

    test(format + ' does not guess a foliate section for legacy progress with no overall fraction', async () => {
        const { reader, messages, directRestores } = openingHarness();
        assert.equal(await reader.open({ url: 'fixture.' + format, format, location: 'chapter:2;fraction:0.25' }), false);
        assert.deepEqual(directRestores, []);
        assert.equal(reader.snapshot(), null);
        assert.equal(messages.some(message => message.type === 'reader-error'), true);
    });
}
