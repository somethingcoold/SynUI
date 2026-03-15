import express from 'express';
import cors from 'cors';
import fs from 'fs';
import path from 'path';
import http from 'http';
import { WebSocketServer } from 'ws';
import { spawn, exec as execCb } from 'child_process';
import { promisify } from 'util';
import { createRequire } from 'module';
import { fileURLToPath } from 'url';

const exec = promisify(execCb);

const __filename = fileURLToPath(import.meta.url);
const __dirname  = path.dirname(__filename);

const app = express();
app.use(cors());
app.use(express.text({ type: '*/*', limit: '10mb' }));
app.use(express.json());

// ═══════════════════════════════════
//  PATHS  (use __dirname — not cwd)
// ═══════════════════════════════════

const LOCALAPPDATA = process.env.LOCALAPPDATA || process.env.APPDATA;
const dataRoot            = path.join(LOCALAPPDATA, 'SynUI');
const scriptsDir          = path.join(dataRoot, 'scripts');
const autoexecDir         = path.join(dataRoot, 'autoexec');
const stateJsonPath       = path.join(dataRoot, 'state.json');
const synapseZRoot        = path.join(LOCALAPPDATA, 'Synapse Z');
const synapseZAutoexecDir = path.join(synapseZRoot, 'autoexec');
const lspDir              = path.join(synapseZRoot, 'lsp');
// ✅  Fixed: was process.cwd() which breaks when launched from another directory
const binDir              = path.join(__dirname, 'BackendBin');

for (const d of [dataRoot, scriptsDir, autoexecDir, synapseZAutoexecDir, lspDir, binDir])
    fs.mkdirSync(d, { recursive: true });

console.log(`[SynUI] BackendBin → ${binDir}`);

// ═══════════════════════════════════
//  CONSOLE BROADCAST
// ═══════════════════════════════════

/** @type {Set<import('ws').WebSocket>} */
const consoleClients = new Set();
let consoleBuffer    = [];          // keep last 200 for late-joiners

function broadcastConsole(entry) {
    consoleBuffer.push(entry);
    if (consoleBuffer.length > 200) consoleBuffer.shift();

    const msg = JSON.stringify(entry);
    for (const ws of consoleClients) {
        try {
            if (ws.readyState === 1 /* OPEN */) ws.send(msg);
        } catch (e) {
            consoleClients.delete(ws);
        }
    }
}

function makeEntry(type, text) {
    const now = new Date();
    const ts  = now.toTimeString().substring(0, 8);
    return {
        id:   `${Date.now()}-${Math.random().toString(36).substring(7)}`,
        type: (Number.isInteger(type) && type >= 0 && type <= 3) ? type : 0,
        text: String(text).trim(),
        ts,
    };
}

// ═══════════════════════════════════
//  SYNZ API LOADER (cached)
// ═══════════════════════════════════

let _SynzAPI = null;
let _apiLoadError = null;

function loadSynzAPI() {
    if (_SynzAPI) return { api: _SynzAPI, error: null };

    // Locate SynzApi.js  (check BackendBin, __dirname, and Synapse Z install locations)
    const candidates = [
        path.join(binDir, 'SynzApi.js'),
        path.join(__dirname, 'SynzApi.js'),
        path.join(synapseZRoot, 'bin', 'SynzApi.js'),
        path.join(synapseZRoot, 'SynzApi.js'),
        path.join(LOCALAPPDATA, 'Synapse Z', 'bin', 'SynzApi.js'),
    ];
    const found = candidates.find(p => fs.existsSync(p));
    if (!found) {
        const msg = `SynzApi.js not found. Searched in:\n  ${candidates.join('\n  ')}`;
        _apiLoadError = msg;
        return { api: null, error: msg };
    }

    try {
        const req = createRequire(found);
        _SynzAPI = req(found);
        console.log(`[SynUI] SynzApi.js loaded from ${found}`);
        return { api: _SynzAPI, error: null };
    } catch (e) {
        const msg = `Failed to load SynzApi.js: ${e.message}`;
        _apiLoadError = msg;
        console.error(`[SynUI] ${msg}`);
        return { api: null, error: msg };
    }
}

// ═══════════════════════════════════
//  STATE MANAGEMENT
// ═══════════════════════════════════

function loadState() {
    let state = { Tabs: [], ActiveTabId: null };
    if (fs.existsSync(stateJsonPath)) {
        try { state = JSON.parse(fs.readFileSync(stateJsonPath, 'utf8')); }
        catch (e) { console.error('[SynUI] state.json parse error:', e.message); }
    }
    importScriptFiles(state, scriptsDir, false);
    importAutoExecFiles(state, autoexecDir);
    return state;
}

function importScriptFiles(state, dir, isAutoExec) {
    if (!fs.existsSync(dir)) return;
    for (const file of fs.readdirSync(dir)) {
        if (!file.endsWith('.lua')) continue;
        const name = path.parse(file).name;
        if (!state.Tabs.find(t => (t.Name === name || t.Name === file) && t.IsAutoExec === isAutoExec))
            state.Tabs.push({ Id: genId(), Name: name, Content: fs.readFileSync(path.join(dir, file), 'utf8'), IsAutoExec: isAutoExec });
    }
}

function importAutoExecFiles(state, dir) {
    if (!fs.existsSync(dir)) return;
    for (const file of fs.readdirSync(dir)) {
        if (!file.endsWith('.lua')) continue;
        const content = fs.readFileSync(path.join(dir, file), 'utf8');
        const existing = state.Tabs.find(t => (t.Name === file || t.Name + '.lua' === file) && t.IsAutoExec);
        if (existing) existing.Content = content;
        else state.Tabs.push({ Id: genId(), Name: file, Content: content, IsAutoExec: true });
    }
}

function saveState(state) {
    const tmp = stateJsonPath + '.tmp';
    fs.writeFileSync(tmp, JSON.stringify(state, null, 2));
    fs.renameSync(tmp, stateJsonPath);
    if (!state.Tabs) return;

    const validScripts  = new Set();
    const validAutoExec = new Set();

    for (const tab of state.Tabs) {
        const fn = tab.Name.endsWith('.lua') ? tab.Name : tab.Name + '.lua';
        if (tab.IsAutoExec) {
            fs.writeFileSync(path.join(autoexecDir, fn), tab.Content || '');
            try { fs.writeFileSync(path.join(synapseZAutoexecDir, fn), tab.Content || ''); } catch {}
            validAutoExec.add(fn);
        } else {
            fs.writeFileSync(path.join(scriptsDir, fn), tab.Content || '');
            validScripts.add(fn);
        }
    }
    cleanStaleFiles(scriptsDir, validScripts);
    cleanStaleFiles(autoexecDir, validAutoExec);
}

function cleanStaleFiles(dir, valid) {
    if (!fs.existsSync(dir)) return;
    for (const f of fs.readdirSync(dir))
        if (f.endsWith('.lua') && !valid.has(f))
            try { fs.unlinkSync(path.join(dir, f)); } catch {}
}

function genId() {
    return (typeof crypto !== 'undefined' && crypto.randomUUID)
        ? crypto.randomUUID()
        : Math.random().toString(36).substring(7);
}

// ═══════════════════════════════════
//  API ROUTES
// ═══════════════════════════════════

app.get('/api/tabs', (req, res) => res.json(loadState().Tabs || []));

app.post('/api/save', (req, res) => {
    try {
        const newTabs = typeof req.body === 'string' ? JSON.parse(req.body) : req.body;
        const state = loadState();
        state.Tabs = newTabs;
        saveState(state);
        res.send('ok');
    } catch (e) {
        res.status(500).send(e.toString());
    }
});

app.get('/api/weao', async (req, res) => {
    try {
        const r = await fetch('https://weao.xyz/api/status/exploits', { headers: { 'User-Agent': 'WEAO-3PService' } });
        const data = await r.json();
        const synz = data.find(x => x.title?.toLowerCase() === 'synapse z');
        if (synz) res.json({ Success: true, IsUpdated: synz.updateStatus || false, RobloxVersion: synz.rbxversion || 'Unknown' });
        else res.json({ Success: false });
    } catch (e) {
        res.status(500).json({ Success: false, error: e.message });
    }
});

// Execute — file-based scheduler (same approach as C# app, no SynzApi.js required)
app.post('/api/execute', (req, res) => {
    const script = typeof req.body === 'string' ? req.body : JSON.stringify(req.body);
    const pid    = parseInt(req.headers['x-pid'] || '0', 10) || 0;

    const synBinDir      = path.join(synapseZRoot, 'bin');
    const schedulerDir   = path.join(synBinDir, 'scheduler');

    if (!fs.existsSync(synBinDir))
        return res.status(500).json({ status: 1, error: `Synapse Z bin not found: ${synBinDir}` });
    if (!fs.existsSync(schedulerDir))
        return res.status(500).json({ status: 2, error: `Synapse Z scheduler not found: ${schedulerDir}` });

    try {
        const name     = Math.random().toString(36).substring(2, 12) + '.lua';
        const fileName = pid === 0 ? name : `PID${pid}_${name}`;
        fs.writeFileSync(path.join(schedulerDir, fileName), script + '@@FileFullyWritten@@');
        console.log(`[SynUI] Executed → ${fileName}`);
        res.json({ status: 0, error: '' });
    } catch (e) {
        res.status(500).json({ status: 3, error: e.message });
    }
});

// Instances — enumerate RobloxPlayerBeta processes via tasklist
app.get('/api/instances', async (req, res) => {
    try {
        const { stdout } = await exec('tasklist /FI "IMAGENAME eq RobloxPlayerBeta.exe" /FO CSV /NH');
        const instances = stdout.trim().split('\n')
            .filter(l => l.toLowerCase().includes('robloxplayerbeta'))
            .map(l => {
                const parts = l.split(',');
                const pid = parts[1]?.replace(/"/g, '').trim() || '';
                return { name: 'RobloxPlayerBeta', pid };
            })
            .filter(i => i.pid && i.pid !== '0');
        res.json(instances);
    } catch {
        res.json([]);
    }
});

// Hook token — client registers a new token; only messages bearing this token are accepted
let activeHookToken = '';

app.post('/api/hook/register', (req, res) => {
    // Use global crypto (available in Node 19+) same as genId()
    activeHookToken = crypto.randomUUID().replace(/-/g, '').substring(0, 8);
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.json({ token: activeHookToken });
});

app.options('/api/hook/register', (req, res) => {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
    res.status(200).end();
});

// Console — receives POST from Roblox hook script
// Body format: "TOKEN:TYPE:MESSAGE"
app.post('/api/console', (req, res) => {
    res.setHeader('Access-Control-Allow-Origin', '*');
    const body = typeof req.body === 'string' ? req.body : '';

    // Parse token prefix
    const first = body.indexOf(':');
    if (first < 0) { res.status(200).send('ok'); return; }
    const incomingToken = body.substring(0, first);
    const rest          = body.substring(first + 1);

    // Drop messages from stale hooks (when a token is active)
    if (activeHookToken && incomingToken !== activeHookToken) {
        res.status(200).send('ok');
        return;
    }

    // Parse TYPE:MESSAGE
    const second  = rest.indexOf(':');
    const typeRaw = second >= 0 ? parseInt(rest.substring(0, second), 10) : 0;
    const text    = second >= 0 ? rest.substring(second + 1) : rest;

    const entry = makeEntry(typeRaw, text);
    broadcastConsole(entry);

    console.log(`[Console] type=${entry.type} text=${entry.text.substring(0, 80)}`);
    res.status(200).send('ok');
});

app.options('/api/console', (req, res) => {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
    res.status(200).end();
});

// Console test — lets the UI verify the broadcast pipeline
app.post('/api/console/test', (req, res) => {
    broadcastConsole(makeEntry(0, 'Test: print output from SynUI'));
    broadcastConsole(makeEntry(1, 'Test: info message'));
    broadcastConsole(makeEntry(2, 'Test: warning message'));
    broadcastConsole(makeEntry(3, 'Test: error message'));
    res.send('ok');
});

// Settings (theme persistence)
const settingsPath = path.join(dataRoot, 'settings.json');
app.get('/api/settings', (req, res) => {
    try { res.json(fs.existsSync(settingsPath) ? JSON.parse(fs.readFileSync(settingsPath, 'utf8')) : {}); }
    catch { res.json({}); }
});
app.post('/api/settings', (req, res) => {
    try {
        const s = typeof req.body === 'string' ? JSON.parse(req.body) : req.body;
        fs.writeFileSync(settingsPath, JSON.stringify(s, null, 2));
        res.send('ok');
    } catch (e) { res.status(500).send(e.toString()); }
});

// ═══════════════════════════════════
//  STATIC FRONTEND
// ═══════════════════════════════════

const distDir = path.join(__dirname, 'dist');
if (fs.existsSync(distDir)) {
    app.use(express.static(distDir));
    app.use((req, res, next) => {
        const skip = ['/api', '/lsp', '/console'];
        if (skip.some(p => req.path.startsWith(p))) return next();
        res.sendFile(path.join(distDir, 'index.html'));
    });
}

// ═══════════════════════════════════
//  HTTP SERVER + WEBSOCKETS (ws pkg)
// ═══════════════════════════════════

const server = http.createServer(app);

const wssConsole = new WebSocketServer({ noServer: true });
const wssLsp     = new WebSocketServer({ noServer: true });

server.on('upgrade', (req, socket, head) => {
    if (req.url === '/console') {
        wssConsole.handleUpgrade(req, socket, head, ws => wssConsole.emit('connection', ws, req));
    } else if (req.url === '/lsp') {
        wssLsp.handleUpgrade(req, socket, head, ws => wssLsp.emit('connection', ws, req));
    } else {
        socket.destroy();
    }
});

wssConsole.on('connection', (ws) => {
    consoleClients.add(ws);
    ws.send(JSON.stringify(makeEntry(1, `[SynUI] Console connected — ${consoleClients.size} client(s) active`)));
    for (const entry of consoleBuffer) {
        try { ws.send(JSON.stringify(entry)); } catch {}
    }
    ws.on('close', () => consoleClients.delete(ws));
    ws.on('error', () => consoleClients.delete(ws));
});

wssLsp.on('connection', (ws) => {
    const exePath = path.join(lspDir, 'luau-lsp.exe');
    if (!fs.existsSync(exePath)) { ws.close(1011, 'luau-lsp.exe not found'); return; }
    const lsp = spawn(exePath, ['lsp'], { cwd: lspDir });
    lsp.stdout.on('data', d => { if (ws.readyState === 1) ws.send(d); });
    ws.on('message', m => lsp.stdin.write(m));
    ws.on('close', () => lsp.kill());
    lsp.on('exit', () => { if (ws.readyState === 1) ws.close(); });
});

const PORT = 1337;
server.listen(PORT, () => {
    console.log(`[SynUI] Server → http://localhost:${PORT}`);
    console.log(`[SynUI] Data   → ${dataRoot}`);
    console.log(`[SynUI] BinDir → ${binDir}`);
    if (fs.existsSync(distDir)) console.log(`[SynUI] Frontend → ${distDir}`);
});
