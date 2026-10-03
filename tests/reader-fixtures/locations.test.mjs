import { strict as assert } from 'node:assert';
import { pathToFileURL } from 'node:url';
const path = '../../src/SiloPlayer/Assets/Reader/locations.mjs';
let api = {};
try { api = await import(new URL(path, import.meta.url)); } catch {}
assert.equal(typeof api.parseLocation, 'function', 'reader must parse shared locations');
assert.deepEqual(api.parseLocation('fraction:0.75'), {fraction: .75});
assert.deepEqual(api.parseLocation('chapter:2;fraction:0.250000'), {index:2, anchor:.25, legacy:true});
assert.equal(api.parseLocation('chapter:-1;fraction:0'), null);
assert.equal(api.parseLocation('fraction:NaN'), null);
assert.equal(api.parseLocation('nonsense'), null);
assert.equal(api.parseLocation('https://evil.test/x'), null);
assert.equal(api.parseLocation('epubcfi(/6/4!/4/2/1:12)'), 'epubcfi(/6/4!/4/2/1:12)');
assert.equal(api.parseLocation('OPS/ch2.xhtml#target'), 'OPS/ch2.xhtml#target');
assert.equal(api.progressFromRelocate({location:{current:2,total:4},cfi:'epubcfi(/6/6!/4)'}).progress,.75);
assert.equal(api.progressFromRelocate({location:{current:2,total:4}}).location,'fraction:0.750000');
console.log('Reader location contract: PASS');
