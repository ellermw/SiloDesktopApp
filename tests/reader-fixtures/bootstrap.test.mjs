import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

test('module load failures report the native reader generation without leaking exception data', () => {
    const listeners = new Map(), messages = [];
    const global = {
        location:{search:'?generation=7&fileId=42'}, URLSearchParams,
        chrome:{webview:{postMessage:message=>messages.push(JSON.parse(message))}},
        addEventListener:(name,handler)=>listeners.set(name,handler),
    };
    vm.runInNewContext(readFileSync(new URL('../../src/SiloPlayer/Assets/Reader/bootstrap.js',import.meta.url),'utf8'),global);
    listeners.get('error')({target:{tagName:'SCRIPT'},message:'private publication details'});
    listeners.get('unhandledrejection')({reason:new Error('private publication details')});
    assert.deepEqual(messages,[{type:'reader-load-error',hostGeneration:7},{type:'reader-load-error',hostGeneration:7}]);
});

test('errors after the reader shell is available do not become startup failures', () => {
    const listeners = new Map();
    const messages = [];
    const shell = {
        location: { search: '?generation=9&fileId=42' }, URLSearchParams,
        chrome: { webview: { postMessage: message => messages.push(JSON.parse(message)) } },
        addEventListener: (name, handler) => listeners.set(name, handler),
        removeEventListener: (name, handler) => {
            if (listeners.get(name) === handler) listeners.delete(name);
        },
    };
    vm.runInNewContext(readFileSync(new URL('../../src/SiloPlayer/Assets/Reader/bootstrap.js', import.meta.url), 'utf8'), shell);
    shell.siloReader = { snapshot: () => ({ progress: .5 }) };
    listeners.get('error')?.({ target: { tagName: 'SCRIPT' }, message: 'runtime fixture error' });
    listeners.get('unhandledrejection')?.({ reason: new Error('runtime fixture rejection') });
    assert.deepEqual(messages, []);
});
