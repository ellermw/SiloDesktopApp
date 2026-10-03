export function parseLocation(value) {
    if (typeof value !== 'string') return null;
    const text = value.trim();
    const legacy = /^chapter:(\d+);fraction:([\d.eE+-]+)$/.exec(text);
    if (legacy) {
        const index = Number(legacy[1]), anchor = Number(legacy[2]);
        return Number.isSafeInteger(index) && Number.isFinite(anchor) && anchor >= 0 && anchor <= 1
            ? {index, anchor, legacy:true} : null;
    }
    if (text.startsWith('fraction:')) {
        const number = text.slice(9).trim();
        const fraction = number ? Number(number) : NaN;
        return Number.isFinite(fraction) ? {fraction:Math.min(1,Math.max(0,fraction))} : null;
    }
    if (/^epubcfi\(.+\)$/.test(text)) return text;
    if (/^[^:\s]+\.(?:xhtml|html|htm)(?:#[^\s]*)?$/i.test(text) && !text.startsWith('//') && !text.includes('..')) return text;
    return null;
}
export function progressFromRelocate(detail) {
    const current = detail.location?.current ?? 0, total = detail.location?.total ?? 0;
    if (!Number.isFinite(current) || !Number.isFinite(total) || total <= 0 || current < 0) return null;
    const progress = Math.min(1,Math.max(0,(current + 1) / total));
    return {location:detail.cfi?.trim() || `fraction:${progress.toFixed(6)}`,progress};
}
