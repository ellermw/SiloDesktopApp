import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
const fixtures=path.dirname(fileURLToPath(import.meta.url));
const assets=path.resolve(fixtures,'../../src/SiloPlayer/Assets/Reader');
const server=http.createServer((req,res)=>{
    const root=req.url.startsWith('/fixtures/')?fixtures:assets;
    const relative=decodeURIComponent(req.url.startsWith('/fixtures/')?req.url.slice(10):req.url.slice(1)) || 'index.html';
    const file=path.resolve(root,relative);
    if(!file.startsWith(root+path.sep)||!fs.existsSync(file)){res.writeHead(404);return res.end();}
    const types={'.mjs':'text/javascript','.js':'text/javascript','.html':'text/html','.css':'text/css','.wasm':'application/wasm','.epub':'application/epub+zip','.pdf':'application/pdf'};
    res.setHeader('Content-Type',types[path.extname(file)]||'application/octet-stream');fs.createReadStream(file).pipe(res);
});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const port=server.address().port, debugPort=port+1;
const profile=fs.mkdtempSync(path.join(os.tmpdir(),'silo-reader-browser-'));
const edge=spawn('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',['--headless=new','--disable-gpu','--no-first-run',`--user-data-dir=${profile}`,`--remote-debugging-port=${debugPort}`,'about:blank'],{stdio:'ignore',windowsHide:true});
let socket;
try {
    let target;
    for(let i=0;i<80;i++) {try {target=await (await fetch(`http://127.0.0.1:${debugPort}/json/new?http://127.0.0.1:${port}/index.html`,{method:'PUT'})).json();break;}catch{await new Promise(resolve=>setTimeout(resolve,100));}}
    if(!target)throw new Error('Edge debugging endpoint did not start');
    socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise(resolve=>socket.addEventListener('open',resolve,{once:true}));
    let id=0;const pending=new Map();socket.addEventListener('message',({data})=>{const message=JSON.parse(data);if(message.id){const [resolve,reject]=pending.get(message.id)||[];pending.delete(message.id);message.error?reject(message.error):resolve(message.result);}});
    const call=(method,params={})=>new Promise((resolve,reject)=>{const next=++id;pending.set(next,[resolve,reject]);socket.send(JSON.stringify({id:next,method,params}));});
    await call('Runtime.enable');
    for(let i=0;i<100;i++) {const ready=await call('Runtime.evaluate',{expression:'!!window.siloReader',returnByValue:true});if(ready.result.value)break;await new Promise(resolve=>setTimeout(resolve,100));}
    const script=fs.readFileSync(path.join(fixtures,'renderer-checks.js'),'utf8');
    await call('Runtime.evaluate',{expression:script});
    let timeout;
    const result=await Promise.race([call('Runtime.evaluate',{expression:`window.runReaderChecks('http://127.0.0.1:${port}/fixtures')`,awaitPromise:true,returnByValue:true}),new Promise((_,reject)=>{timeout=setTimeout(()=>reject(new Error('Reader fixture timed out')),45000);})]).finally(()=>clearTimeout(timeout));
    if(result.exceptionDetails)throw new Error(JSON.stringify(result.exceptionDetails));
    console.log(JSON.stringify(result.result.value,null,2));
    console.log('PASS: real Chromium renderer EPUB/PDF interoperability fixtures');
    await call('Browser.close');
} finally {socket?.close();edge.kill();server.close();}
