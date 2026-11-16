import { makeBook, SEARCH_RESULT_LIMIT } from './vendor/foliate-js/view.js';
import { Overlayer } from './vendor/foliate-js/overlayer.js';
import { parseLocation, progressFromRelocate } from './locations.mjs';

const hostGeneration = Number(new URLSearchParams(location.search).get('generation') ?? 0);
const fileId = Number(new URLSearchParams(location.search).get('fileId') ?? 0);
const send = message => globalThis.chrome?.webview?.postMessage(JSON.stringify({...message,hostGeneration}));
let view, book, generation = 0, ready = false, snapshot = null, annotations = [], chapterPaths = [], format;
let activeSearch = null;
const clearSearch = () => {
    activeSearch?.abort();
    activeSearch = null;
    view?.clearSearch();
};
const contents = () => view?.renderer?.getContents?.() ?? [];
// Use the publication's navigation document, not its reading-order spine.
const tableOfContents = (items = book?.toc ?? [], depth = 0, entries = []) => {
    for (const item of items) {
        if (entries.length >= 2048 || depth > 32) break;
        entries.push({label:String(item.label ?? ''), href:String(item.href ?? ''), depth});
        tableOfContents(item.subitems ?? [], depth + 1, entries);
    }
    return entries;
};
const error = message => send({type:'reader-error', message});
// Interface text comes from the native shell as an explicit same-key payload
// (EbookReaderPage.Localization.cs). The shell's language never sets the
// document direction: the book keeps its own direction and page order.
let localeStrings = {};
const text = (key, english) => typeof localeStrings[key] === 'string' && localeStrings[key] ? localeStrings[key] : english;
const openFailed = () => text('web.reader.error.openFailed', 'Unable to open ebook');
const locationUnresolved = () => text('desktop.reader.locationUnresolved', 'This saved reading location could not be resolved. Choose a chapter or move the progress slider to continue.');
const locationUnavailable = () => text('desktop.reader.locationUnavailable', 'This reading location is unavailable in this file.');

async function navigate(location) {
    const activeView = view, activeBook = book, current = generation, paths = chapterPaths;
    const target = parseLocation(location) ?? (tableOfContents().some(item => item.href === location) ? location : null);
    if (!target || !view) return false;
    let resolved;
    if (target.legacy) {
        if (format === 'pdf') {
            if (target.index !== 0) return false;
            resolved = await activeView.resolveNavigation({fraction:target.anchor});
        } else if (format !== 'epub') {
            resolved = {index:target.index, anchor:target.anchor};
        } else {
        // Old desktop chapters omit non-HTML spine items. Resolve by path rather than
        // treating the old chapter number as an EPUB spine number.
        const path = paths[target.index];
        const index = activeBook.sections.findIndex(s => s.id === path || decodeURI(String(s.id)) === path);
        if (index < 0) return false;
        resolved = {index, anchor:target.anchor};
        }
    } else resolved = await activeView.resolveNavigation(target);
    if (current !== generation || !resolved || !Number.isInteger(resolved.index) || !activeBook.sections[resolved.index]) return false;
    // Foliate's public goTo catches errors and can leave the old page in place.
    // Validate CFI/href anchors against the actual document before accepting a restore.
    if (typeof resolved.anchor === 'function' && !(activeView.isFixedLayout && typeof target === 'string' && /^epubcfi\(\/6\/\d+\)$/.test(target))) {
        const doc = await activeBook.sections[resolved.index].createDocument?.();
        if (!doc) return false;
        if (current !== generation) return false;
        const anchor = resolved.anchor(doc);
        if (anchor == null) return false;
    }
    if (current !== generation) return false;
    await activeView.renderer.goTo(resolved);
    return current === generation;
}

async function drawAnnotations() {
    const activeView = view, current = generation;
    for (const annotation of annotations) {
        if (current !== generation) return;
        if (annotation.kind === 'bookmark') continue;
        if (!annotation.cfiRange && !annotation.localCfi && annotation.selectedText) {
            const legacy = parseLocation(annotation.location);
            if (legacy?.legacy) {
                const chapter = chapterPaths[legacy.index];
                const content = contents().find(({index}) => book.sections[index]?.id === chapter);
                if (content) {
                    const walker = content.doc.createTreeWalker(content.doc.body, NodeFilter.SHOW_TEXT);
                    for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                        const offset = node.nodeValue.indexOf(annotation.selectedText);
                        if (offset < 0) continue;
                        const range = content.doc.createRange();
                        range.setStart(node, offset); range.setEnd(node, offset + annotation.selectedText.length);
                        annotation.localCfi = activeView.getCFI(content.index, range);
                        break;
                    }
                }
            }
        }
        const value = annotation.cfiRange || annotation.localCfi;
        if (!value) continue;
        try { await activeView.addAnnotation({value, color:annotation.color || '#facc15'}); }
        catch { /* An annotation for another edition must not prevent reading. */ }
    }
}

window.siloReader = {
    locale(payload) {
        localeStrings = payload && typeof payload.strings === 'object' && payload.strings ? {...payload.strings} : {};
        if (typeof payload?.language === 'string' && payload.language) document.documentElement.lang = payload.language;
    },
    async open(options) {
        clearSearch();
        const current = ++generation;
        ready = false; snapshot = null;
        view?.close(); view?.remove(); book?.destroy?.();
        annotations = options.annotations ?? []; chapterPaths = options.chapters ?? []; format = options.format;
        // A missing or blocked local book rejects fetch itself; word both failures the same way.
        const response = await fetch(options.url).catch(() => null);
        if (current !== generation) return false;
        if (!response?.ok) throw new Error(openFailed());
        const file = new File([await response.blob()], `book.${options.format}`);
        if (current !== generation) return false;
        const nextBook = await makeBook(file);
        if (current !== generation) { nextBook.destroy?.(); return false; }
        book = nextBook;
        // Script-disabled iframe sandboxes and the shell CSP also block inline
        // handlers and network activity originating in untrusted book content.
        book.transformTarget?.addEventListener('load', event => {
            if (event.detail.isScript) event.detail.allow = false;
        });
        view = document.createElement('foliate-view');
        const activeView = view;
        document.body.replaceChildren(view);
        view.addEventListener('external-link', event => event.preventDefault());
        view.addEventListener('draw-annotation', ({detail}) => detail.draw(Overlayer.highlight, {color:detail.annotation.color}));
        view.addEventListener('load', ({detail:{doc,index}}) => {
            if (current !== generation) return;
            doc.addEventListener('selectionchange', () => send({type:'selection',text:doc.getSelection()?.toString() ?? ''}));
            void drawAnnotations();
        });
        view.addEventListener('relocate', ({detail}) => {
            if (current !== generation) return;
            const progress = progressFromRelocate(detail);
            if (!progress) return;
            const index = detail.section?.current ?? activeView.renderer.primaryIndex ?? 0;
            const chapterIndex = chapterPaths.findIndex(path => path === nextBook.sections[index]?.id || path === decodeURI(String(nextBook.sections[index]?.id)));
            snapshot = {...progress,index,chapterIndex,count:nextBook.sections.length};
            if (ready) send({type:'relocate',...snapshot});
        });
        await activeView.open(nextBook);
        if (current !== generation) return false;
        this.appearance(options.settings ?? {});
        const legacyFb2 = (format === 'fb2' || format === 'fbz') && parseLocation(options.location)?.legacy;
        const location = options.location?.startsWith('fraction:') && !parseLocation(options.location) && Number.isFinite(options.progress)
            ? `fraction:${options.progress}` : options.location;
        if (legacyFb2) {
            if (!Number.isFinite(options.progress) || options.progress < 0 || options.progress > 1) {
                send({type:'reader-error',message:locationUnresolved(),count:book.sections.length,toc:tableOfContents()});
                return false;
            }
            await activeView.goToFraction(options.progress);
        } else if (location) {
            if (!await navigate(location)) {
                if (current !== generation) return false;
                send({type:'reader-error',message:locationUnresolved(),count:book.sections.length,toc:tableOfContents()});
                return false;
            }
        } else if (Number.isFinite(options.progress) && options.progress > 0) {
            await activeView.goToFraction(options.progress);
        } else await activeView.init({showTextStart:true});
        if (current !== generation) return false;
        ready = true;
        if (snapshot) send({type:'relocate',...snapshot});
        send({type:'reader-ready',count:book.sections.length,toc:tableOfContents()});
        await drawAnnotations();
        return true;
    },
    async go(location) {
        const current = generation;
        try {
            if (!await navigate(location)) { if (current === generation) error(locationUnavailable()); return false; }
            if (current !== generation) return false;
            ready = true; if (snapshot) send({type:'relocate',...snapshot});
            send({type:'reader-ready',count:book.sections.length,toc:tableOfContents()});
            return true;
        } catch { if (current === generation) error(locationUnavailable()); return false; }
    },
    async chapter(index) {
        const current = generation, activeView = view;
        if (!book?.sections[index]) return false;
        await activeView.renderer.goTo({index,anchor:0});
        if (current !== generation) return false;
        ready = true;
        send({type:'reader-ready',count:book.sections.length,toc:tableOfContents()});
        if (snapshot) send({type:'relocate',...snapshot});
        return true;
    },
    snapshot: () => ready ? snapshot : null,
    next: () => view?.next(), prev: () => view?.prev(),
    selection() {
        for (const {doc,index} of contents()) {
            const selection = doc.getSelection();
            if (selection?.rangeCount && !selection.isCollapsed && selection.toString().trim()) {
                return {text:selection.toString().trim(),cfi:view.getCFI(index,selection.getRangeAt(0))};
            }
        }
        return null;
    },
    text: () => contents().map(({doc}) => doc.getSelection()?.toString() || doc.body?.innerText || doc.documentElement.textContent || '').join('\n'),
    async annotations(values) {
        const current = generation, activeView = view;
        for (const old of annotations) if (old.cfiRange || old.localCfi) {
            try { await activeView?.addAnnotation({value:old.cfiRange || old.localCfi},true); } catch {}
        }
        if (current !== generation) return;
        annotations = values; await drawAnnotations();
    },
    clearSearch,
    async find(query, searchId) {
        clearSearch();
        const activeView = view, current = generation, controller = new AbortController();
        activeSearch = controller;
        const { signal } = controller;
        const items = [];
        let truncated = false;
        try {
            if (!ready || !activeView?.search) throw new Error("Search renderer unavailable");
            for await (const result of activeView.search({query, limit:SEARCH_RESULT_LIMIT, signal})) {
                if (signal.aborted || current !== generation || activeView !== view) return null;
                if (result.truncated) truncated = true;
                for (const hit of result.subitems ?? [result]) if (hit.cfi) items.push({cfi:hit.cfi,snippet:typeof hit.excerpt === 'string' ? hit.excerpt : [hit.excerpt?.pre, hit.excerpt?.match, hit.excerpt?.post].join(''),chapterTitle:result.label ?? result.section?.label ?? ''});
            }
        } catch (error) {
            if (signal.aborted || current !== generation || activeView !== view) return null;
            return {items:[], truncated:false, searchId, hostGeneration, fileId, failed:true};
        } finally {
            if (activeSearch === controller) activeSearch = null;
        }
        if (signal.aborted || current !== generation || activeView !== view) return null;
        return {items, truncated, searchId, hostGeneration, fileId};
    },
    appearance(s) {
        if (!view?.renderer) return;
        const colors = s.theme === 'dark' ? ['#111827','#f8fafc'] : s.theme === 'sepia' ? ['#f4ecd8','#2f261b'] : ['#ffffff','#171717'];
        document.body.style.background = colors[0];
        view.renderer.setStyles?.(`:root{--theme-bg-color:${colors[0]};--theme-fg-color:${colors[1]};--override-color:true;color-scheme:${s.theme==='dark'?'dark':'light'}}html,body{background:${colors[0]}!important;color:${colors[1]}!important;font-family:${s.fontFamily || 'inherit'}!important;font-size:${s.fontSize || 112}%!important;font-weight:${s.fontWeight || 400}!important;line-height:${s.lineHeight || 1.65}!important;hyphens:${s.hyphenation===false?'none':'auto'}!important;direction:${s.rtl?'rtl':'inherit'}!important;writing-mode:${s.writingMode==='auto'?'inherit':s.writingMode || 'inherit'}!important;filter:brightness(${s.fontBrightness || 100}%)!important}body :where(p,span,div,li,blockquote,h1,h2,h3,h4,h5,h6,em,strong){color:${colors[1]}!important}`);
        const r = view.renderer;
        r.setAttribute('spread',s.spread==='none'?'none':'auto');
        r.setAttribute('gap','7%'); r.setAttribute('margin',`${s.margin ?? 24}px`);
        r.setAttribute('max-inline-size',s.flow==='scrolled'?'9999px':`${(s.maxWidth || 74)*10}px`);
        r.setAttribute('max-column-count',s.spread==='none'||s.flow==='scrolled'?'1':'2');
        if (s.flow==='scrolled') r.setAttribute('flow','scrolled'); else r.removeAttribute('flow');
        void r.render?.();
    },
    close() { clearSearch(); ++generation; ready=false; snapshot=null; view?.close(); view?.remove(); book?.destroy?.(); }
};
send({type:'shell-ready'});
