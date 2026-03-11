import express from 'express';
import expressWs from 'express-ws';
import cors from 'cors';
import fs from 'fs';
import path from 'path';
import { spawn } from 'child_process';
import { createRequire } from 'module';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
expressWs(app);
app.use(cors());
app.use(express.text({ type: '*/*' }));
app.use(express.json());

// ═══════════════════════════════════
//  CENTRALIZED PATHS
// ═══════════════════════════════════

const LOCALAPPDATA = process.env.LOCALAPPDATA || process.env.APPDATA;

// UI-owned data
const dataRoot = path.join(LOCALAPPDATA, 'SynUI');
const scriptsDir = path.join(dataRoot, 'scripts');
const autoexecDir = path.join(dataRoot, 'autoexec');
const stateJsonPath = path.join(dataRoot, 'state.json');

// Synapse Z executor paths
const synapseZRoot = path.join(LOCALAPPDATA, 'Synapse Z');
const synapseZAutoexecDir = path.join(synapseZRoot, 'autoexec');
const lspDir = path.join(synapseZRoot, 'lsp');
const binDir = path.join(process.cwd(), 'BackendBin');

// Ensure all dirs exist
fs.mkdirSync(dataRoot, { recursive: true });
fs.mkdirSync(scriptsDir, { recursive: true });
fs.mkdirSync(autoexecDir, { recursive: true });
fs.mkdirSync(synapseZAutoexecDir, { recursive: true });
fs.mkdirSync(lspDir, { recursive: true });
fs.mkdirSync(binDir, { recursive: true });

// ═══════════════════════════════════
//  STATE MANAGEMENT
// ═══════════════════════════════════

function loadState() {
    let state = { Tabs: [], ActiveTabId: null };
    if (fs.existsSync(stateJsonPath)) {
        try {
            state = JSON.parse(fs.readFileSync(stateJsonPath, 'utf8'));
        } catch (e) {
            console.error('[SynUI] Failed to parse state.json:', e.message);
        }
    }

    // Import extra .lua files from scripts dir that aren't in state yet
    if (fs.existsSync(scriptsDir)) {
        for (const file of fs.readdirSync(scriptsDir)) {
            if (!file.endsWith('.lua')) continue;
            const name = path.parse(file).name;
            const existing = state.Tabs.find(t => 
                (t.Name === name || t.Name === file) && !t.IsAutoExec
            );
            if (!existing) {
                const content = fs.readFileSync(path.join(scriptsDir, file), 'utf8');
                state.Tabs.push({
                    Id: crypto.randomUUID?.() || Math.random().toString(36).substring(7),
                    Name: name,
                    Content: content,
                    IsAutoExec: false
                });
            }
        }
    }

    // Import autoexec files from UI-owned autoexec dir only
    // We no longer import manually added files from the executor's autoexec dir
    importAutoExecFiles(state, autoexecDir);

    return state;
}

function importAutoExecFiles(state, dir) {
    if (!fs.existsSync(dir)) return;
    for (const file of fs.readdirSync(dir)) {
        if (!file.endsWith('.lua')) continue;
        const fp = path.join(dir, file);
        const content = fs.readFileSync(fp, 'utf8');
        const existing = state.Tabs.find(t =>
            (t.Name === file || t.Name + '.lua' === file) && t.IsAutoExec
        );
        if (existing) {
            existing.Content = content;
        } else {
            state.Tabs.push({
                Id: crypto.randomUUID?.() || Math.random().toString(36).substring(7),
                Name: file,
                Content: content,
                IsAutoExec: true
            });
        }
    }
}

function saveState(state) {
    // Atomic write: temp file → rename
    const tempPath = stateJsonPath + '.tmp';
    fs.writeFileSync(tempPath, JSON.stringify(state, null, 4));
    fs.renameSync(tempPath, stateJsonPath);

    if (!state.Tabs) return;

    const validScriptFiles = new Set();
    const validAutoExecFiles = new Set();

    for (const tab of state.Tabs) {
        const fileName = tab.Name.endsWith('.lua') ? tab.Name : tab.Name + '.lua';

        if (tab.IsAutoExec) {
            // Dual-write: UI autoexec + executor autoexec
            fs.writeFileSync(path.join(autoexecDir, fileName), tab.Content || '');
            fs.writeFileSync(path.join(synapseZAutoexecDir, fileName), tab.Content || '');
            validAutoExecFiles.add(fileName);
        } else {
            fs.writeFileSync(path.join(scriptsDir, fileName), tab.Content || '');
            validScriptFiles.add(fileName);
        }
    }

    // Cleanup stale script files
    cleanupStaleFiles(scriptsDir, validScriptFiles);

    // Cleanup stale UI autoexec files
    cleanupStaleFiles(autoexecDir, validAutoExecFiles);
}

function cleanupStaleFiles(dir, validFiles) {
    if (!fs.existsSync(dir)) return;
    for (const file of fs.readdirSync(dir)) {
        if (file.endsWith('.lua') && !validFiles.has(file)) {
            try { fs.unlinkSync(path.join(dir, file)); } catch { }
        }
    }
}

// ═══════════════════════════════════
//  API ROUTES
// ═══════════════════════════════════

app.get('/api/tabs', (req, res) => {
    const state = loadState();
    res.json(state.Tabs || []);
});

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
        const response = await fetch('https://weao.xyz/api/status/exploits', {
            headers: { 'User-Agent': 'WEAO-3PService' }
        });
        const data = await response.json();
        const synapse = data.find(x => x.title?.toLowerCase() === 'synapse z');
        if (synapse) {
            res.json({
                Success: true,
                IsUpdated: synapse.updateStatus || false,
                RobloxVersion: synapse.rbxversion || 'Unknown'
            });
        } else {
            res.json({ Success: false });
        }
    } catch (e) {
        res.status(500).json({ Success: false, error: e.message });
    }
});

app.post('/api/execute', (req, res) => {
    const script = typeof req.body === 'string' ? req.body : JSON.stringify(req.body);
    try {
        const require = createRequire(path.join(binDir, 'dummy.js'));
        let SynzAPI;
        try {
            SynzAPI = require('./SynzApi.js');
        } catch (err) {
            return res.status(500).json({ status: -1, error: "SynzApi.js not found in BackendBin." });
        }

        let execRes = SynzAPI.Execute(script);
        res.json({ status: execRes || 0, error: "" });
    } catch (e) {
        res.status(500).json({ status: -1, error: `JS Execution Host Crash: ${e.message}` });
    }
});

// ═══════════════════════════════════
//  LSP WEBSOCKET PROXY
// ═══════════════════════════════════

app.ws('/lsp', (ws, req) => {
    const exePath = path.join(lspDir, 'luau-lsp.exe');
    if (!fs.existsSync(exePath)) {
        ws.close(1011, 'luau-lsp.exe not found');
        return;
    }

    const lspProcess = spawn(exePath, ['lsp'], { cwd: lspDir });

    lspProcess.stdout.on('data', (data) => {
        if (ws.readyState === ws.OPEN) {
            ws.send(data);
        }
    });

    ws.on('message', (msg) => {
        lspProcess.stdin.write(msg);
    });

    ws.on('close', () => {
        lspProcess.kill();
    });

    lspProcess.on('exit', () => {
        if (ws.readyState === ws.OPEN) ws.close();
    });
});

// ═══════════════════════════════════
//  STATIC FILE SERVING (built Vite app)
// ═══════════════════════════════════

// Serve pre-built Vite frontend from dist/ if it exists
const distDir = path.join(__dirname, 'dist');
if (fs.existsSync(distDir)) {
    app.use(express.static(distDir));
    // SPA fallback: serve index.html for all non-API routes
    app.use((req, res, next) => {
        if (!req.path.startsWith('/api') && !req.path.startsWith('/lsp')) {
            res.sendFile(path.join(distDir, 'index.html'));
        } else {
            next();
        }
    });
}

// ═══════════════════════════════════
//  START SERVER
// ═══════════════════════════════════

const PORT = 1337;
app.listen(PORT, () => {
    console.log(`[SynUI Node API] Started on http://localhost:${PORT}`);
    console.log(`[SynUI Node API] Data root: ${dataRoot}`);
    if (fs.existsSync(distDir)) {
        console.log(`[SynUI Node API] Serving frontend from ${distDir}`);
    }
    console.log(`[SynUI Node API] Waiting for SynzApi.js and synznativeapi.node in ${binDir}`);
});
