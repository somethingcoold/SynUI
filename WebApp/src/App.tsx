import { useState, useEffect, useRef, useCallback } from 'react';
import Editor, { useMonaco } from '@monaco-editor/react';
import {
  Play, Plus, X, Terminal, ChevronDown, Wifi, WifiOff,
  Zap, FileCode, RotateCcw, Link, Settings, Home,
  ArrowLeft, FlaskConical, Check
} from 'lucide-react';
import './index.css';

// ─── Types ────────────────────────────────────────────────
interface ScriptTab   { Id: string; Name: string; Content: string; IsAutoExec: boolean; }
interface ConsoleEntry { id: string; type: 0|1|2|3; text: string; ts: string; }
interface RbxInstance  { name: string; pid: string; }
type View       = 'welcome' | 'editor' | 'settings';
type FilterType = 'all' | 'print' | 'info' | 'warn' | 'error';
type ThemeId    = 'dark' | 'gray' | 'blue' | 'bluedark';
type StatusKind = 'idle' | 'ok' | 'err' | 'info';

// ─── Theme presets ────────────────────────────────────────
const THEMES: Record<ThemeId, Record<string, string>> = {
  dark: {
    '--bg':'#09090B','--bg-1':'#0D0D10','--bg-2':'#111115','--bg-3':'#18181D',
    '--bg-h':'#1C1C22','--bg-a':'#22222A',
    '--border':'#1E1E26','--border-2':'#27272F',
    '--text':'#E4E4E7','--text-2':'#A1A1AA','--text-m':'#52525B',
    '--purple':'#7C3AED','--purple-h':'#8B5CF6','--purple-d':'#6D28D9',
    '--blue':'#3B82F6','--green':'#10B981','--amber':'#F59E0B','--red':'#EF4444',
  },
  gray: {
    '--bg':'#111113','--bg-1':'#18181B','--bg-2':'#1C1C1F','--bg-3':'#232327',
    '--bg-h':'#27272A','--bg-a':'#2F2F33',
    '--border':'#27272A','--border-2':'#3F3F46',
    '--text':'#FAFAFA','--text-2':'#D4D4D8','--text-m':'#71717A',
    '--purple':'#A1A1AA','--purple-h':'#D4D4D8','--purple-d':'#71717A',
    '--blue':'#6366F1','--green':'#22C55E','--amber':'#F59E0B','--red':'#EF4444',
  },
  blue: {
    '--bg':'#0A1628','--bg-1':'#0D1B30','--bg-2':'#112240','--bg-3':'#152A50',
    '--bg-h':'#1A3360','--bg-a':'#1E3C70',
    '--border':'#1E3A5F','--border-2':'#1E4080',
    '--text':'#E2F0FF','--text-2':'#93C5FD','--text-m':'#4E6E8E',
    '--purple':'#60A5FA','--purple-h':'#93C5FD','--purple-d':'#3B82F6',
    '--blue':'#34D399','--green':'#10B981','--amber':'#FBBF24','--red':'#F87171',
  },
  bluedark: {
    '--bg':'#080C14','--bg-1':'#0C1220','--bg-2':'#10182A','--bg-3':'#141E34',
    '--bg-h':'#18243E','--bg-a':'#1C2A48',
    '--border':'#1E2D40','--border-2':'#243650',
    '--text':'#E2E8F0','--text-2':'#94A3B8','--text-m':'#475569',
    '--purple':'#3B82F6','--purple-h':'#60A5FA','--purple-d':'#2563EB',
    '--blue':'#8B5CF6','--green':'#10B981','--amber':'#FBBF24','--red':'#EF4444',
  },
};

const THEME_META: Record<ThemeId, { label: string; preview: string[] }> = {
  dark:     { label: 'Dark',      preview: ['#09090B','#7C3AED','#E4E4E7','#3B82F6'] },
  gray:     { label: 'Gray',      preview: ['#111113','#A1A1AA','#FAFAFA','#6366F1'] },
  blue:     { label: 'Blue',      preview: ['#0A1628','#60A5FA','#E2F0FF','#34D399'] },
  bluedark: { label: 'Blue Dark', preview: ['#080C14','#3B82F6','#E2E8F0','#8B5CF6'] },
};

function applyTheme(id: ThemeId) {
  const vars = THEMES[id];
  const root = document.documentElement;
  for (const [k, v] of Object.entries(vars)) root.style.setProperty(k, v);
  localStorage.setItem('synui-theme', id);
}

const HOOK_SCRIPT = (port: number, token: string) => `
if _G._SynUIHook then pcall(function() _G._SynUIHook:Disconnect() end) _G._SynUIHook = nil end
local ls  = game:GetService("LogService")
local _tk = "${token}"
local _t  = {
  [Enum.MessageType.MessageOutput]  = 0,
  [Enum.MessageType.MessageInfo]    = 1,
  [Enum.MessageType.MessageWarning] = 2,
  [Enum.MessageType.MessageError]   = 3,
}
_G._SynUIHook = ls.MessageOut:Connect(function(msg, mt)
  pcall(function()
    local fn = (type(syn)=="table" and type(syn.request)=="function" and syn.request)
            or (type(request)=="function" and request)
    if fn then
      fn({
        Url     = "http://127.0.0.1:${port}/api/console",
        Method  = "POST",
        Headers = { ["Content-Type"] = "text/plain" },
        Body    = _tk .. ":" .. tostring(_t[mt] or 0) .. ":" .. tostring(msg),
      })
    end
  end)
end)
print("[SynUI] Console hooked!")
`.trim();

const TYPE_LABEL = ['OUT','INF','WRN','ERR'] as const;
const TYPE_CLASS = ['c-print','c-info','c-warn','c-error'] as const;

// ─── App ─────────────────────────────────────────────────
export default function App() {
  const [view,           setView]           = useState<View>('welcome');
  const [theme,          setTheme]          = useState<ThemeId>('dark');
  const [tabs,           setTabs]           = useState<ScriptTab[]>([]);
  const [activeTabId,    setActiveTabId]    = useState<string|null>(null);
  const [status,         setStatus]         = useState<{text:string;kind:StatusKind}>({text:'Ready',kind:'idle'});
  const [isUpdated,      setIsUpdated]      = useState(false);
  const [robloxVersion,  setRobloxVersion]  = useState('Unknown');
  const [consoleEntries, setConsoleEntries] = useState<ConsoleEntry[]>([]);
  const [terminalOpen,   setTerminalOpen]   = useState(true);
  const [terminalHeight, setTerminalHeight] = useState(220);
  const [terminalFilter, setTerminalFilter] = useState<FilterType>('all');
  const [instances,      setInstances]      = useState<RbxInstance[]>([]);
  const [selectedPid,    setSelectedPid]    = useState('0');
  const [editingId,      setEditingId]      = useState<string|null>(null);
  const [editingName,    setEditingName]    = useState('');
  const [autoScroll,     setAutoScroll]     = useState(true);

  const editorRef        = useRef<any>(null);
  const monaco           = useMonaco();
  const lspSocketRef     = useRef<WebSocket|null>(null);
  const consoleSocketRef = useRef<WebSocket|null>(null);
  const terminalEndRef   = useRef<HTMLDivElement>(null);
  const isDraggingRef    = useRef(false);
  const dragStartYRef    = useRef(0);
  const dragStartHRef    = useRef(0);

  // ── Theme init ──────────────────────────────────────────
  useEffect(() => {
    const saved = (localStorage.getItem('synui-theme') as ThemeId) || 'dark';
    setTheme(saved);
    applyTheme(saved);
  }, []);

  // ── WEAO ────────────────────────────────────────────────
  useEffect(() => {
    fetch('http://localhost:1337/api/weao')
      .then(r => r.json())
      .then(d => { setIsUpdated(!!d.IsUpdated); setRobloxVersion(d.RobloxVersion || 'Unknown'); })
      .catch(() => {});
  }, []);

  // ── Load tabs ───────────────────────────────────────────
  useEffect(() => {
    fetch('http://localhost:1337/api/tabs')
      .then(r => r.json())
      .then((d: ScriptTab[]) => {
        setTabs(d);
        if (d.length > 0) setActiveTabId(d[0].Id);
        // Stay on welcome view — user explicitly navigates
      })
      .catch(() => showStatus('Backend offline — run node server.js', 'err'));
  }, []);

  // ── Poll instances ──────────────────────────────────────
  useEffect(() => {
    const poll = () =>
      fetch('http://localhost:1337/api/instances')
        .then(r => r.json())
        .then((d: RbxInstance[]) => setInstances(d || []))
        .catch(() => {});
    poll();
    const id = setInterval(poll, 3000);
    return () => clearInterval(id);
  }, []);

  // ── Monaco theme ────────────────────────────────────────
  useEffect(() => {
    if (!monaco) return;
    monaco.editor.defineTheme('synui', {
      base: 'vs-dark', inherit: true,
      rules: [
        { token:'keyword',   foreground:'a78bfa', fontStyle:'bold' },
        { token:'string',    foreground:'6ee7b7' },
        { token:'number',    foreground:'fb923c' },
        { token:'comment',   foreground:'52525b', fontStyle:'italic' },
        { token:'identifier',foreground:'e4e4e7' },
        { token:'delimiter', foreground:'71717a' },
      ],
      colors: {
        'editor.background':'#09090B','editor.foreground':'#e4e4e7',
        'editor.lineHighlightBackground':'#111115','editor.selectionBackground':'#7c3aed33',
        'editorLineNumber.foreground':'#3f3f46','editorLineNumber.activeForeground':'#7c3aed',
        'editorCursor.foreground':'#7c3aed','editorGutter.background':'#09090B',
        'editorWidget.background':'#111115','editorWidget.border':'#27272a',
        'editorSuggestWidget.background':'#111115','editorSuggestWidget.border':'#27272a',
        'editorSuggestWidget.selectedBackground':'#1e1e24',
      },
    });
    monaco.editor.setTheme('synui');
    const ws = new WebSocket('ws://localhost:1337/lsp');
    lspSocketRef.current = ws;
    return () => ws.close();
  }, [monaco]);

  // ── Console WebSocket ───────────────────────────────────
  useEffect(() => {
    let alive = true;
    const connect = () => {
      if (!alive) return;
      const ws = new WebSocket('ws://localhost:1337/console');
      consoleSocketRef.current = ws;
      ws.onmessage = (e) => {
        try {
          const entry = JSON.parse(e.data) as ConsoleEntry;
          setConsoleEntries(prev => [...prev.slice(-1499), entry]);
        } catch {}
      };
      ws.onclose = () => { if (alive) setTimeout(connect, 2000); };
      ws.onerror = () => { if (alive) setTimeout(connect, 3000); };
    };
    connect();
    return () => { alive = false; consoleSocketRef.current?.close(); };
  }, []);

  // ── Auto-scroll ─────────────────────────────────────────
  useEffect(() => {
    if (autoScroll && terminalEndRef.current)
      terminalEndRef.current.scrollIntoView({ behavior: 'auto' });
  }, [consoleEntries, autoScroll]);

  // ── Drag resize ─────────────────────────────────────────
  useEffect(() => {
    const onMove = (e: MouseEvent) => {
      if (!isDraggingRef.current) return;
      const delta = dragStartYRef.current - e.clientY;
      setTerminalHeight(Math.max(80, Math.min(600, dragStartHRef.current + delta)));
    };
    const onUp = () => { isDraggingRef.current = false; };
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', onUp);
    return () => { window.removeEventListener('mousemove', onMove); window.removeEventListener('mouseup', onUp); };
  }, []);

  // ── Keyboard ────────────────────────────────────────────
  useEffect(() => {
    const h = (e: KeyboardEvent) => {
      if ((e.ctrlKey||e.metaKey) && e.key === 'Enter') { e.preventDefault(); executeCode(); }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  });

  const activeTab = tabs.find(t => t.Id === activeTabId);

  const showStatus = (text: string, kind: StatusKind, ms = 3500) => {
    setStatus({ text, kind });
    if (ms < 999000) setTimeout(() => setStatus({ text:'Ready', kind:'idle' }), ms);
  };

  const saveState = async (newTabs: ScriptTab[]) => {
    setTabs(newTabs);
    await fetch('http://localhost:1337/api/save', { method:'POST', body: JSON.stringify(newTabs) }).catch(() => {});
  };

  const handleEditorChange = (value?: string) => {
    if (!activeTabId || value === undefined) return;
    saveState(tabs.map(t => t.Id === activeTabId ? { ...t, Content: value } : t));
    if (lspSocketRef.current?.readyState === WebSocket.OPEN) {
      const payload = JSON.stringify({ jsonrpc:'2.0', method:'textDocument/didChange',
        params:{ textDocument:{ uri:`file:///${activeTabId}.lua`, version:2 }, contentChanges:[{ text:value }] } });
      lspSocketRef.current.send(`Content-Length: ${new TextEncoder().encode(payload).length}\r\n\r\n${payload}`);
    }
  };

  const executeCode = useCallback(async () => {
    if (!activeTab) return;
    showStatus('Executing…','info',999999);
    try {
      const res = await fetch('http://localhost:1337/api/execute', {
        method:'POST', headers:{'X-PID': selectedPid}, body: activeTab.Content });
      const d = await res.json();
      if (d.status === 0) showStatus('Executed successfully','ok');
      else showStatus(d.error || 'Execution error','err');
    } catch { showStatus('Backend offline','err'); }
  }, [activeTab, selectedPid]);

  const hookConsole = async () => {
    showStatus('Injecting hook…','info',999999);
    try {
      // Register a fresh token — server will reject messages from all prior hooks
      const regRes = await fetch('http://localhost:1337/api/hook/register', { method: 'POST' });
      const { token } = await regRes.json();

      const res = await fetch('http://localhost:1337/api/execute', {
        method:'POST', headers:{'X-PID': selectedPid}, body: HOOK_SCRIPT(1337, token) });
      const d = await res.json();
      showStatus(d.status === 0 ? 'Console hooked!' : 'Hook failed', d.status === 0 ? 'ok' : 'err');
    } catch { showStatus('Backend offline','err'); }
  };

  const testConsole = async () => {
    await fetch('http://localhost:1337/api/console/test', { method:'POST' }).catch(() => {});
    showStatus('Test messages sent','ok');
  };

  const addNewTab = (isAutoExec: boolean) => {
    const id   = crypto.randomUUID?.() ?? Math.random().toString(36).substring(7);
    const name = `Script ${tabs.length + 1}`;
    const newTab: ScriptTab = { Id:id, Name:name, Content:'-- Start scripting here\n', IsAutoExec:isAutoExec };
    saveState([...tabs, newTab]);
    setActiveTabId(id);
    setEditingId(id);
    setEditingName(name);
    setView('editor');
    if (lspSocketRef.current?.readyState === WebSocket.OPEN) {
      const payload = JSON.stringify({ jsonrpc:'2.0', method:'textDocument/didOpen',
        params:{ textDocument:{ uri:`file:///${id}.lua`, languageId:'luau', version:1, text:newTab.Content } } });
      lspSocketRef.current.send(`Content-Length: ${new TextEncoder().encode(payload).length}\r\n\r\n${payload}`);
    }
  };

  const handleRename = () => {
    if (!editingId || !editingName.trim()) { setEditingId(null); return; }
    saveState(tabs.map(t => t.Id === editingId ? { ...t, Name: editingName.trim() } : t));
    setEditingId(null);
  };

  const deleteScript = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const newTabs = tabs.filter(t => t.Id !== id);
    if (activeTabId === id) { setActiveTabId(newTabs[0]?.Id ?? null); if (!newTabs.length) setView('welcome'); }
    saveState(newTabs);
  };

  const changeTheme = (id: ThemeId) => {
    setTheme(id);
    applyTheme(id);
    fetch('http://localhost:1337/api/settings', { method:'POST', body: JSON.stringify({ theme: id }) }).catch(() => {});
  };

  const filteredConsole = consoleEntries.filter(e => {
    if (terminalFilter === 'all') return true;
    return (['print','info','warn','error'] as FilterType[]).indexOf(terminalFilter) === e.type;
  });

  const standardTabs = tabs.filter(t => !t.IsAutoExec);
  const autoExecTabs = tabs.filter(t =>  t.IsAutoExec);

  // ── SIDEBAR (shared across all views) ─────────────────
  const Sidebar = (
    <aside className="sidebar">
      <div className="sidebar-logo">
        <span className="logo-mark">◈</span>
        <span className="logo-word">SYN<em>UI</em></span>
        <div className="logo-actions">
          <button className="logo-icon-btn" onClick={() => setView('welcome')} title="Home"><Home size={13}/></button>
          <button className="logo-icon-btn" onClick={() => setView('settings')} title="Settings"><Settings size={13}/></button>
        </div>
      </div>

      <div className="sidebar-body">
        <div className="script-group">
          <div className="group-hdr">
            <span className="group-label">SCRIPTS</span>
            <button className="icon-btn" onClick={() => addNewTab(false)} title="New Script"><Plus size={12}/></button>
          </div>
          {standardTabs.map(t => (
            <div key={t.Id} className={`script-item ${activeTabId===t.Id && view==='editor' ? 'active' : ''}`}>
              {editingId===t.Id ? (
                <input autoFocus className="rename-inp" value={editingName}
                  onChange={e => setEditingName(e.target.value)}
                  onBlur={handleRename}
                  onKeyDown={e => { if (e.key==='Enter') handleRename(); if (e.key==='Escape') setEditingId(null); }}/>
              ) : (<>
                <FileCode size={12} className="si-icon"/>
                <button className="si-name" onClick={() => { setActiveTabId(t.Id); setView('editor'); }}
                  onDoubleClick={() => { setEditingId(t.Id); setEditingName(t.Name); }}>{t.Name}</button>
                <button className="si-del" onClick={e => deleteScript(t.Id, e)}><X size={10}/></button>
              </>)}
            </div>
          ))}
        </div>

        <div className="script-group ae-group">
          <div className="group-hdr">
            <span className="group-label ae-label">AUTO EXEC</span>
            <button className="icon-btn ae-btn" onClick={() => addNewTab(true)} title="New AutoExec"><Plus size={12}/></button>
          </div>
          {autoExecTabs.map(t => (
            <div key={t.Id} className={`script-item ae-item ${activeTabId===t.Id && view==='editor' ? 'active ae-active' : ''}`}>
              {editingId===t.Id ? (
                <input autoFocus className="rename-inp" value={editingName}
                  onChange={e => setEditingName(e.target.value)}
                  onBlur={handleRename}
                  onKeyDown={e => { if (e.key==='Enter') handleRename(); if (e.key==='Escape') setEditingId(null); }}/>
              ) : (<>
                <Zap size={12} className="si-icon ae-icon"/>
                <button className="si-name" onClick={() => { setActiveTabId(t.Id); setView('editor'); }}
                  onDoubleClick={() => { setEditingId(t.Id); setEditingName(t.Name); }}>{t.Name}</button>
                <button className="si-del" onClick={e => deleteScript(t.Id, e)}><X size={10}/></button>
              </>)}
            </div>
          ))}
        </div>
      </div>

      <div className="sidebar-foot">
        <div className={`synz-badge ${isUpdated?'ok':'bad'}`}>
          {isUpdated ? <Wifi size={9}/> : <WifiOff size={9}/>}
          <span>Synapse Z {isUpdated?'Functional':'Outdated'}</span>
        </div>
        <span className="rbx-ver">Roblox {robloxVersion}</span>
      </div>
    </aside>
  );

  // ── WELCOME VIEW ────────────────────────────────────────
  if (view === 'welcome') return (
    <div className="app">
      {Sidebar}
      <main className="main welcome-main">
        <div className="welcome-center">
          <div className="welcome-logo">
            <span className="wl-mark">◈</span>
            <h1 className="wl-title">SYNUI</h1>
          </div>

          <div className="welcome-actions">
            <button className="wa-primary" onClick={() => addNewTab(false)}>
              <Plus size={18}/>
              <span>New Script</span>
            </button>
            <button className="wa-secondary" onClick={() => setView('settings')}>
              <Settings size={16}/>
              <span>Settings</span>
            </button>
          </div>

          <div className="welcome-features">
            <div className="wf-item"><span className="wf-dot purple"/>Auto-complete Luau script editor</div>
            <div className="wf-item"><span className="wf-dot blue"/>Console logging &amp; output capture</div>
            <div className="wf-item"><span className="wf-dot green"/>Multi-instance &amp; PID targeting</div>
            <div className="wf-item"><span className="wf-dot amber"/>Auto-exec script support</div>
          </div>

          {tabs.length > 0 && (
            <button className="wa-ghost" onClick={() => setView('editor')}>
              Continue to editor →
            </button>
          )}
        </div>
      </main>
    </div>
  );

  // ── SETTINGS VIEW ───────────────────────────────────────
  if (view === 'settings') return (
    <div className="app">
      {Sidebar}
      <main className="main settings-main">
        <div className="settings-topbar">
          <button className="back-btn" onClick={() => setView(tabs.length > 0 ? 'editor' : 'welcome')}>
            <ArrowLeft size={14}/> Back
          </button>
          <span className="settings-title">Settings</span>
        </div>

        <div className="settings-body">
          {/* Theme */}
          <section className="settings-section">
            <div className="ss-label">THEME</div>
            <div className="theme-grid">
              {(Object.keys(THEMES) as ThemeId[]).map(id => {
                const meta = THEME_META[id];
                const vars = THEMES[id];
                return (
                  <button key={id}
                    className={`theme-card ${theme===id?'active':''}`}
                    onClick={() => changeTheme(id)}
                    style={{ background: vars['--bg'], borderColor: theme===id ? vars['--purple'] : vars['--border'] }}>
                    <div className="tc-swatches">
                      {meta.preview.map((c,i) => <span key={i} className="tc-swatch" style={{background:c}}/>)}
                    </div>
                    <span className="tc-label" style={{ color: vars['--text-2'] }}>{meta.label}</span>
                    {theme===id && (
                      <span className="tc-check" style={{ background: vars['--purple'] }}>
                        <Check size={10} color="#fff"/>
                      </span>
                    )}
                  </button>
                );
              })}
            </div>
          </section>

          {/* Console */}
          <section className="settings-section">
            <div className="ss-label">CONSOLE DEBUG</div>
            <div className="ss-desc">Send test messages to verify the console pipeline is working.</div>
            <button className="ss-btn" onClick={testConsole}>
              <FlaskConical size={13}/> Test Console Output
            </button>
          </section>

        </div>
      </main>
    </div>
  );

  // ── EDITOR VIEW ─────────────────────────────────────────
  return (
    <div className="app">
      {Sidebar}
      <main className="main">
        {/* Topbar */}
        <div className="topbar">
          <div className="topbar-l">
            <span className="active-file">{activeTab?.Name ?? 'No file open'}</span>
            {activeTab?.IsAutoExec && <span className="ae-badge">AUTO EXEC</span>}
          </div>
          <div className="topbar-r">
            <div className="pid-wrap">
              <select className="pid-select" value={selectedPid} onChange={e => setSelectedPid(e.target.value)}>
                <option value="0">All Instances</option>
                {instances.map(i => <option key={i.pid} value={i.pid}>PID {i.pid}</option>)}
              </select>
              <ChevronDown size={11} className="pid-arrow"/>
            </div>
            <button className="exec-btn" onClick={executeCode} title="Execute (Ctrl+Enter)"><Play size={12} fill="currentColor"/> Execute</button>
          </div>
        </div>

        {/* Editor */}
        <div className="editor-wrap">
          <Editor
            height="100%" language="lua" theme="synui"
            value={activeTab?.Content ?? ''}
            onChange={handleEditorChange}
            onMount={e => { editorRef.current = e; }}
            options={{
              minimap:{enabled:false}, fontSize:13,
              fontFamily:"'JetBrains Mono','Cascadia Code',Consolas,monospace",
              fontLigatures:true, lineNumbers:'on',
              padding:{top:14,bottom:14}, scrollBeyondLastLine:false,
              smoothScrolling:true, cursorBlinking:'phase',
              cursorSmoothCaretAnimation:'on', renderLineHighlight:'all',
              overviewRulerBorder:false, folding:true,
              bracketPairColorization:{enabled:true}, tabSize:2,
            }}
          />
        </div>

        {/* Terminal drag handle */}
        {terminalOpen && (
          <div className="term-drag"
            onMouseDown={e => {
              isDraggingRef.current = true;
              dragStartYRef.current = e.clientY;
              dragStartHRef.current = terminalHeight;
              e.preventDefault();
            }}/>
        )}

        {/* Terminal */}
        {terminalOpen && (
          <div className="terminal" style={{height: terminalHeight}}>
            <div className="term-hdr">
              <div className="term-title">
                <Terminal size={12}/><span>CONSOLE</span>
                {filteredConsole.length > 0 && <span className="term-count">{filteredConsole.length}</span>}
                <button className="hook-btn" onClick={hookConsole} title="Inject console hook"><Link size={11}/> Hook</button>
              </div>
              <div className="term-ctrls">
                {(['all','print','info','warn','error'] as FilterType[]).map(f => (
                  <button key={f} className={`flt-btn flt-${f} ${terminalFilter===f?'on':''}`}
                    onClick={() => setTerminalFilter(f)}>
                    {f==='print'?'OUT':f.toUpperCase()}
                  </button>
                ))}
                <div className="term-sep"/>
                <button className={`flt-btn ${autoScroll?'on':''}`} onClick={() => setAutoScroll(p=>!p)} title="Auto-scroll">SCROLL</button>
                <button className="flt-btn" onClick={testConsole} title="Send test messages"><FlaskConical size={10}/></button>
                <button className="flt-btn" onClick={() => setConsoleEntries([])} title="Clear"><RotateCcw size={10}/></button>
                <button className="flt-btn" onClick={() => setTerminalOpen(false)} title="Hide"><X size={10}/></button>
              </div>
            </div>
            <div className="term-body">
              {filteredConsole.length === 0 && (
                <div className="term-empty">
                  No output — click <strong>Hook</strong> to capture Roblox console, or <strong>🧪</strong> to test
                </div>
              )}
              {filteredConsole.map(e => (
                <div key={e.id} className={`con-line ${TYPE_CLASS[e.type]}`}>
                  <span className="con-ts">{e.ts}</span>
                  <span className={`con-tag ${TYPE_CLASS[e.type]}`}>{TYPE_LABEL[e.type]}</span>
                  <span className="con-txt">{e.text}</span>
                </div>
              ))}
              <div ref={terminalEndRef}/>
            </div>
          </div>
        )}

        {/* Status bar */}
        <div className="statusbar">
          <div className={`stat-pill ${status.kind}`}>
            <span className="stat-dot"/><span>{status.text}</span>
          </div>
          {!terminalOpen && (
            <button className="stat-action" onClick={() => setTerminalOpen(true)}><Terminal size={10}/> Console</button>
          )}
          <div className="stat-r">
            <span className={`weao-dot ${isUpdated?'ok':'bad'}`}/>
            <span>{isUpdated?'Synapse Z: OK':'Synapse Z: Down'}</span>
            <span className="stat-sep">·</span>
            <span>{tabs.length} scripts</span>
            <span className="stat-sep">·</span>
            <span className="stat-hint">Ctrl+Enter</span>
          </div>
        </div>
      </main>
    </div>
  );
}
