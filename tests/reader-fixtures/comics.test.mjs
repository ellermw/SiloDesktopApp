import assert from 'node:assert/strict';
import { test } from 'node:test';
import { makeComicBook } from '../../src/SiloPlayer/Assets/Reader/vendor/foliate-js/comic-book.js';

test('comic navigation retains uppercase image extensions accepted by native extraction', async () => {
    const filenames = ['001.JPG', '002.PnG', '003.AVIF', '004.JXL', 'notes.txt'];
    const book = await makeComicBook({
        entries: filenames.map(filename => ({ filename })),
        getSize: () => 1,
    }, { name: 'fixture.cbz' });
    assert.deepEqual(book.sections.map(section => section.id), filenames.slice(0, 4));
    assert.deepEqual(book.toc.map(item => item.href), filenames.slice(0, 4));
    assert.equal(book.resolveHref('002.PnG').index, 1);
});

test('mixed-case comic page names preserve the legacy desktop reading order', async () => {
    const book = await makeComicBook({
        entries: ['c.PNG', 'B.jpg', 'a.JPG'].map(filename => ({ filename })),
        getSize: () => 1,
    }, { name: 'mixed-case.cbz' });
    assert.deepEqual(book.sections.map(section => section.id), ['a.JPG', 'B.jpg', 'c.PNG']);
    assert.deepEqual(book.toc.map(item => item.href), ['a.JPG', 'B.jpg', 'c.PNG']);
    assert.equal(book.resolveHref('B.jpg').index, 1);
});
