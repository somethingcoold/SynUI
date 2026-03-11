import { useState, useEffect, useRef } from 'react';
import Editor, { useMonaco } from '@monaco-editor/react';
import { Play, Plus, Trash2 } from 'lucide-react';
import './index.css';

interface ScriptTab {
  Id: string;
  Name: string;
  Content: string;
  IsAutoExec: boolean;
}

export default function App() {
  const [tabs, setTabs] = useState<ScriptTab[]>([]);
  const [activeTabId, setActiveTabId] = useState<string | null>(null);
  const [status, setStatus] = useState<string>('Ready');
  const [isUpdated, setIsUpdated] = useState(false);
  const [robloxVersion, setRobloxVersion] = useState("Unknown");
  const editorRef = useRef<any>(null);
  const monaco = useMonaco();
  const lspSocketRef = useRef<WebSocket | null>(null);

  // Fetch WEAO status
  useEffect(() => {
    fetch('http://localhost:1337/api/weao')
      .then(res => res.json())
      .then(data => {
        if (data.Success) {
          setIsUpdated(data.IsUpdated);
          setRobloxVersion(data.RobloxVersion);
        }
      })
      .catch(console.error);
  }, []);

  // Load tabs on mount
  useEffect(() => {
    fetch('http://localhost:1337/api/tabs')
      .then(res => res.json())
      .then(data => {
        setTabs(data);
        if (data.length > 0 && !activeTabId) setActiveTabId(data[0].Id);
      })
      .catch(() => setStatus('Backend disconnected. Run node server.js'));
  }, []);

  // Set up Luau syntax & LSP Bridge natively!
  useEffect(() => {
    if (monaco) {
      monaco.editor.defineTheme('synapse-dark', {
        base: 'vs-dark',
        inherit: true,
        rules: [
          { token: 'keyword', foreground: '7c5cfc', fontStyle: 'bold' },
          { token: 'string', foreground: '5b8def' },
          { token: 'number', foreground: 'ff7b72' },
          { token: 'comment', foreground: '6e7681', fontStyle: 'italic' },
        ],
        colors: {
          'editor.background': '#0D0D0F',
          'editor.lineHighlightBackground': '#1C1C21',
          'editorLineNumber.foreground': '#484F58',
          'editorIndentGuide.background': '#21262d',
        }
      });
      monaco.editor.setTheme('synapse-dark');

      const ws = new WebSocket('ws://localhost:1337/lsp');
      lspSocketRef.current = ws;

      ws.onopen = () => {
        // Emulate `textDocument/didOpen` if needed natively,
        // but for a pure sleek UX, LSP connects in bg.
      };

      ws.onclose = () => { };
    }
  }, [monaco]);

  const activeTab = tabs.find(t => t.Id === activeTabId);

  const saveState = async (newTabs: ScriptTab[]) => {
    setTabs(newTabs);
    await fetch('http://localhost:1337/api/save', {
      method: 'POST',
      body: JSON.stringify(newTabs)
    }).catch(console.error);
  };

  const handleEditorChange = (value: string | undefined) => {
    if (!activeTabId || value === undefined) return;
    const newTabs = tabs.map(t => t.Id === activeTabId ? { ...t, Content: value } : t);
    saveState(newTabs);

    // Forward raw JSON-RPC textDocument/didChange
    if (lspSocketRef.current?.readyState === WebSocket.OPEN) {
      const payload = JSON.stringify({
        jsonrpc: '2.0',
        method: 'textDocument/didChange',
        params: {
          textDocument: { uri: `file:///${activeTabId}.lua`, version: 2 },
          contentChanges: [{ text: value }]
        }
      });
      const data = `Content-Length: ${new TextEncoder().encode(payload).length}\r\n\r\n${payload}`;
      lspSocketRef.current.send(data);
    }
  };

  const executeCode = async () => {
    if (!activeTab) return;
    setStatus('Executing...');
    try {
      const res = await fetch('http://localhost:1337/api/execute', {
        method: 'POST',
        body: activeTab.Content
      });
      const data = await res.json();
      if (data.status === 0) setStatus('Execution successful!');
      else setStatus(`Error Executing: ${data.error}`);
    } catch (e) {
      setStatus('Execution failed (Backend down)');
    }
  };

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState<string>('');

  const addNewTab = (isAutoExec: boolean) => {
    const newId = Math.random().toString(36).substring(7);
    const nameStr = `New Script ${tabs.length + 1}` + (isAutoExec ? '.lua' : '');
    const newTab: ScriptTab = {
      Id: newId,
      Name: nameStr,
      Content: '-- Start scripting here\n',
      IsAutoExec: isAutoExec
    };
    saveState([...tabs, newTab]);
    setActiveTabId(newTab.Id);

    // Automatically start editing the new tab's name
    setEditingId(newId);
    setEditingName(nameStr);

    if (lspSocketRef.current?.readyState === WebSocket.OPEN) {
      const payload = JSON.stringify({
        jsonrpc: '2.0',
        method: 'textDocument/didOpen',
        params: {
          textDocument: { uri: `file:///${newId}.lua`, languageId: 'luau', version: 1, text: newTab.Content }
        }
      });
      const data = `Content-Length: ${new TextEncoder().encode(payload).length}\r\n\r\n${payload}`;
      lspSocketRef.current.send(data);
    }
  };

  const handleRename = () => {
    if (!editingId || !editingName.trim()) {
      setEditingId(null);
      return;
    }
    const newTabs = tabs.map(t => t.Id === editingId ? { ...t, Name: editingName.trim() } : t);
    saveState(newTabs);
    setEditingId(null);
  };

  const deleteScript = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const newTabs = tabs.filter(t => t.Id !== id);
    if (activeTabId === id) {
      setActiveTabId(newTabs.length > 0 ? newTabs[0].Id : null);
    }
    saveState(newTabs);
  };

  const standardTabs = tabs.filter(t => !t.IsAutoExec);
  const autoExecTabs = tabs.filter(t => t.IsAutoExec);

  return (
    <div className="layout-container">
      {/* Sidebar */}
      <div className="sidebar">
        <div className="sidebar-header">
          <h1 className="logo-text">
            SYNUI <span className="logo-subtext">WEB</span>
          </h1>
        </div>

        <div className="sidebar-scrollable">
          {/* Scripts Section */}
          <div className="nav-section">
            <div className="nav-header">
              <span className="nav-title">SCRIPTS</span>
              <button onClick={() => addNewTab(false)} className="add-btn hover-glow">
                <Plus size={14} />
              </button>
            </div>
            <div className="nav-list">
              {standardTabs.map(t => (
                <div key={t.Id} className="nav-item-container">
                  {editingId === t.Id ? (
                    <input
                      autoFocus
                      className="nav-rename-input"
                      value={editingName}
                      onChange={(e) => setEditingName(e.target.value)}
                      onBlur={handleRename}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') handleRename();
                        if (e.key === 'Escape') setEditingId(null);
                      }}
                    />
                  ) : (
                    <div className={`nav-item-wrapper ${activeTabId === t.Id ? 'active' : ''}`}>
                      <button
                        onClick={() => setActiveTabId(t.Id)}
                        onDoubleClick={() => {
                          setEditingId(t.Id);
                          setEditingName(t.Name);
                        }}
                        className="nav-item"
                      >
                        {activeTabId === t.Id && <div className="nav-indicator"></div>}
                        {t.Name}
                      </button>
                      <button className="nav-delete-btn" onClick={(e) => deleteScript(t.Id, e)}>
                        <Trash2 size={12} />
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>

          {/* Auto Exec Section */}
          <div className="nav-section border-top">
            <div className="nav-header">
              <span className="nav-title autoexec-title">AUTO EXEC</span>
              <button onClick={() => addNewTab(true)} className="add-btn autoexec-glow">
                <Plus size={14} />
              </button>
            </div>
            <div className="nav-list">
              {autoExecTabs.map(t => (
                <div key={t.Id} className="nav-item-container">
                  {editingId === t.Id ? (
                    <input
                      autoFocus
                      className="nav-rename-input"
                      value={editingName}
                      onChange={(e) => setEditingName(e.target.value)}
                      onBlur={handleRename}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') handleRename();
                        if (e.key === 'Escape') setEditingId(null);
                      }}
                    />
                  ) : (
                    <div className={`nav-item-wrapper ${activeTabId === t.Id ? 'active' : ''}`}>
                      <button
                        onClick={() => setActiveTabId(t.Id)}
                        onDoubleClick={() => {
                          setEditingId(t.Id);
                          setEditingName(t.Name);
                        }}
                        className="nav-item"
                      >
                        {activeTabId === t.Id && <div className="nav-indicator autoexec-indicator"></div>}
                        {t.Name}
                      </button>
                      <button className="nav-delete-btn" onClick={(e) => deleteScript(t.Id, e)}>
                        <Trash2 size={12} />
                      </button>
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      {/* Main Content */}
      <div className="main-content">
        {/* Topbar */}
        <div className="topbar">
          <div className="topbar-left">
            {activeTab?.Name}
            {activeTab?.IsAutoExec && (
              <span className="autoexec-badge">AutoExec</span>
            )}
          </div>
          <div className="topbar-right">
            <span className="roblox-version">
              Roblox Version: {robloxVersion}
            </span>
            <div className={`weao-status ${isUpdated ? 'functional' : 'outdated'}`}>
              <div className={`weao-dot ${isUpdated ? 'functional' : 'outdated'}`}></div>
              {isUpdated ? 'Functional' : 'Outdated'}
            </div>
            <button onClick={executeCode} className="execute-btn">
              <Play size={14} fill="currentColor" /> Execute
            </button>
          </div>
        </div>

        {/* Editor */}
        <div className="editor-container">
          <Editor
            height="100%"
            language="lua"
            theme="synapse-dark"
            value={activeTab?.Content || ''}
            onChange={handleEditorChange}
            onMount={(editor) => { editorRef.current = editor; }}
            options={{
              minimap: { enabled: false },
              padding: { top: 16 },
              fontSize: 14,
              fontFamily: "'Inter', 'Consolas', monospace",
              scrollBeyondLastLine: false,
              smoothScrolling: true,
              cursorBlinking: "smooth",
              cursorSmoothCaretAnimation: "on",
              renderLineHighlight: "all",
            }}
          />
        </div>

        {/* Bottom bar */}
        <div className="bottombar">
          <span className="bottom-status">{status}</span>
        </div>
      </div>
    </div>
  );
}
