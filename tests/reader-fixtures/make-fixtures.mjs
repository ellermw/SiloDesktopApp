import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const directory = path.dirname(fileURLToPath(import.meta.url));
const crc32 = buffer => {
    let crc = -1;
    for (const byte of buffer) { crc ^= byte; for(let n=0;n<8;n++) crc = (crc>>>1)^((crc&1)?0xedb88320:0); }
    return (crc^-1)>>>0;
};
const files = {
    'mimetype':'application/epub+zip',
    'META-INF/container.xml':'<?xml version="1.0"?><container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="OPS/package.opf" media-type="application/oebps-package+xml"/></rootfiles></container>',
    'OPS/package.opf':'<?xml version="1.0"?><package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">reader-fixture</dc:identifier><dc:title>Unequal chapters</dc:title><dc:language>en</dc:language></metadata><manifest><item id="short" href="short.xhtml" media-type="application/xhtml+xml"/><item id="long" href="long.xhtml" media-type="application/xhtml+xml"/></manifest><spine><itemref idref="short"/><itemref idref="long"/></spine></package>',
    'OPS/short.xhtml':'<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Short</title></head><body><p>Short opening.</p></body></html>',
    'OPS/long.xhtml':'<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Long</title><script>parent.__bookScriptExecuted=true</script></head><body>'+Array.from({length:100},(_,i)=>`<p id="p${i}">Paragraph ${i}. ${'Distinct passage for reading and annotation. '.repeat(8)}</p>`).join('')+'</body></html>'
};
let offset=0; const data=[], directoryEntries=[];
for(const [name,value] of Object.entries(files)) {
    const text=Buffer.from(value), filename=Buffer.from(name), crc=crc32(text);
    const local=Buffer.alloc(30); local.writeUInt32LE(0x04034b50); local.writeUInt16LE(20,4); local.writeUInt32LE(crc,14); local.writeUInt32LE(text.length,18); local.writeUInt32LE(text.length,22); local.writeUInt16LE(filename.length,26);
    const central=Buffer.alloc(46); central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20,4); central.writeUInt16LE(20,6); central.writeUInt32LE(crc,16); central.writeUInt32LE(text.length,20); central.writeUInt32LE(text.length,24); central.writeUInt16LE(filename.length,28); central.writeUInt32LE(offset,42);
    data.push(local,filename,text); directoryEntries.push(central,filename); offset+=local.length+filename.length+text.length;
}
const cd=Buffer.concat(directoryEntries), end=Buffer.alloc(22);end.writeUInt32LE(0x06054b50);end.writeUInt16LE(Object.keys(files).length,8);end.writeUInt16LE(Object.keys(files).length,10);end.writeUInt32LE(cd.length,12);end.writeUInt32LE(offset,16);
fs.writeFileSync(path.join(directory,'unequal.epub'),Buffer.concat([...data,cd,end]));
const objects=['<< /Type /Catalog /Pages 2 0 R >>','<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R 9 0 R] /Count 4 >>'];
for(let i=0;i<4;i++) {
    objects.push(`<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 700] /Resources << /Font << /F1 11 0 R >> >> /Contents ${4+i*2} 0 R >>`);
    const stream=`BT /F1 24 Tf 50 600 Td (PDF fixture page ${i+1}) Tj ET`;
    objects.push(`<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`);
}
objects.push('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>');
let pdf='%PDF-1.4\n', offsets=[0];
objects.forEach((object,i)=>{offsets.push(Buffer.byteLength(pdf));pdf+=`${i+1} 0 obj\n${object}\nendobj\n`;});
const xref=Buffer.byteLength(pdf);pdf+=`xref\n0 ${objects.length+1}\n0000000000 65535 f \n`+offsets.slice(1).map(x=>`${String(x).padStart(10,'0')} 00000 n \n`).join('')+`trailer << /Size ${objects.length+1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
fs.writeFileSync(path.join(directory,'four-pages.pdf'),pdf);
console.log('Generated deterministic EPUB and four-page PDF fixtures.');
