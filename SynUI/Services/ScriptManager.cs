using System;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using SynUI.Models;

namespace SynUI.Services
{
    public static class ScriptManager
    {
        private static Timer? _debounceTimer;
        private static readonly object _saveLock = new();
        private static AppState? _pendingState;

        static ScriptManager()
        {
            // Trigger AppPaths static initialization (ensures directories exist)
            _ = AppPaths.DataRoot;

            // Perform one-time migration from legacy locations
            Migrator.MigrateAll();
        }

        /// <summary>
        /// Triggers early initialization of paths and migration.
        /// Call this at startup to front-load any I/O.
        /// </summary>
        public static void Initialize()
        {
            // Static constructor already ran — this is just a convenient entry point
        }

        public static void Save(AppState state)
        {
            lock (_saveLock)
            {
                _pendingState = state;

                // Debounce: wait 200ms before flushing to disk
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(_ => FlushToDisk(), null, 200, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Immediately flush the pending state to disk (bypasses debounce).
        /// Call this on app shutdown to ensure nothing is lost.
        /// </summary>
        public static void FlushNow()
        {
            lock (_saveLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
            FlushToDisk();
        }

        private static void FlushToDisk()
        {
            AppState? state;
            lock (_saveLock)
            {
                state = _pendingState;
                _pendingState = null;
            }

            if (state == null) return;

            try
            {
                // Atomic write: write to temp file, then rename
                string json = JsonConvert.SerializeObject(state, Formatting.Indented);
                string tempPath = AppPaths.StateFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, AppPaths.StateFilePath, overwrite: true);

                // Sync .lua files to disk
                SyncScriptFilesToDisk(state);
                SyncAutoExecFilesToDisk(state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes regular (non-autoexec) scripts to %LOCALAPPDATA%\SynUI\scripts\
        /// </summary>
        private static void SyncScriptFilesToDisk(AppState state)
        {
            try
            {
                var validFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var tab in state.Tabs.Where(t => !t.IsAutoExec))
                {
                    string fileName = EnsureLuaExtension(tab.Name);
                    string filePath = Path.Combine(AppPaths.ScriptsDir, fileName);
                    File.WriteAllText(filePath, tab.Content);
                    validFiles.Add(fileName);
                }

                // Cleanup stale .lua files
                foreach (var file in Directory.GetFiles(AppPaths.ScriptsDir, "*.lua"))
                {
                    if (!validFiles.Contains(Path.GetFileName(file)))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScriptManager] Cleanup error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] SyncScriptFilesToDisk error: {ex.Message}");
            }
        }

        /// <summary>
        /// Writes autoexec scripts to BOTH:
        /// - %LOCALAPPDATA%\SynUI\autoexec\ (UI-owned)
        /// - %LOCALAPPDATA%\Synapse Z\autoexec\ (executor-consumed)
        /// </summary>
        private static void SyncAutoExecFilesToDisk(AppState state)
        {
            try
            {
                var autoExecTabs = state.Tabs.Where(t => t.IsAutoExec).ToList();
                var validFiles = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var tab in autoExecTabs)
                {
                    string fileName = EnsureLuaExtension(tab.Name);
                    validFiles.Add(fileName);

                    // Write to UI-owned autoexec
                    string uiPath = Path.Combine(AppPaths.AutoExecDir, fileName);
                    File.WriteAllText(uiPath, tab.Content);

                    // Write to executor's autoexec
                    string executorPath = Path.Combine(AppPaths.SynapseZAutoExecDir, fileName);
                    File.WriteAllText(executorPath, tab.Content);
                }

                // Cleanup stale files from UI autoexec dir
                CleanupStaleFiles(AppPaths.AutoExecDir, validFiles);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] SyncAutoExecFilesToDisk error: {ex.Message}");
            }
        }

        public static AppState Load()
        {
            AppState? currentState = null;
            try
            {
                if (File.Exists(AppPaths.StateFilePath))
                {
                    string json = File.ReadAllText(AppPaths.StateFilePath);
                    currentState = JsonConvert.DeserializeObject<AppState>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] Load error: {ex.Message}");
            }

            if (currentState == null)
            {
                currentState = new AppState();
                currentState.Tabs.Add(new ScriptTab { Name = "Script 1", Content = "-- Write your Lua script here\n" });
                currentState.ActiveTabId = currentState.Tabs[0].Id;
            }

            // Sync with physical files
            SyncStateFromScriptFiles(currentState);
            SyncAutoExecFilesFromDisk(currentState);

            return currentState;
        }

        /// <summary>
        /// Updates content of existing tabs from .lua files on disk.
        /// Does NOT import new files — only syncs content for tabs already in state.
        /// </summary>
        private static void SyncStateFromScriptFiles(AppState state)
        {
            try
            {
                if (!Directory.Exists(AppPaths.ScriptsDir)) return;

                foreach (var file in Directory.GetFiles(AppPaths.ScriptsDir, "*.lua"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    var existing = state.Tabs.FirstOrDefault(t =>
                        (t.Name == name || t.Name == Path.GetFileName(file)) && !t.IsAutoExec);

                    // Only update content of tabs already in state — don't add new ones
                    if (existing != null)
                        existing.Content = File.ReadAllText(file);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] SyncStateFromScriptFiles error: {ex.Message}");
            }
        }

        /// <summary>
        /// Imports autoexec files from the UI-owned autoexec dir only.
        /// We no longer auto-import manually added files from the executor's dir.
        /// </summary>
        private static void SyncAutoExecFilesFromDisk(AppState state)
        {
            try
            {
                // Primary: read from UI-owned autoexec
                ImportAutoExecFiles(state, AppPaths.AutoExecDir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] SyncAutoExecFilesFromDisk error: {ex.Message}");
            }
        }

        private static void ImportAutoExecFiles(AppState state, string directory)
        {
            if (!Directory.Exists(directory)) return;

            foreach (var filePath in Directory.GetFiles(directory, "*.lua"))
            {
                string fileName = Path.GetFileName(filePath);
                string content = File.ReadAllText(filePath);

                var existingTab = state.Tabs.FirstOrDefault(t =>
                    (t.Name == fileName || t.Name + ".lua" == fileName) && t.IsAutoExec);

                if (existingTab != null)
                    existingTab.Content = content;
                else
                {
                    state.Tabs.Add(new ScriptTab
                    {
                        Name = fileName,
                        Content = content,
                        IsAutoExec = true
                    });
                }
            }
        }

        /// <summary>
        /// Removes an autoexec file from BOTH the UI-owned and executor autoexec directories.
        /// </summary>
        public static void RemoveAutoExecFile(string tabName)
        {
            try
            {
                string fileName = EnsureLuaExtension(tabName);

                // Remove from UI autoexec
                string uiPath = Path.Combine(AppPaths.AutoExecDir, fileName);
                if (File.Exists(uiPath)) File.Delete(uiPath);

                // Remove from executor autoexec
                string executorPath = Path.Combine(AppPaths.SynapseZAutoExecDir, fileName);
                if (File.Exists(executorPath)) File.Delete(executorPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] RemoveAutoExecFile error: {ex.Message}");
            }
        }

        public static void DeleteScriptFile(string name, bool isAutoExec)
        {
            try
            {
                string path = isAutoExec 
                    ? Path.Combine(AppPaths.AutoExecDir, name) 
                    : Path.Combine(AppPaths.ScriptsDir, name);
                
                if (!path.EndsWith(".lua")) path += ".lua";

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                // If it was an autoexec, also delete the executor's copy
                if (isAutoExec)
                {
                    string execCopy = Path.Combine(AppPaths.SynapseZAutoExecDir, Path.GetFileName(path));
                    if (File.Exists(execCopy))
                    {
                        File.Delete(execCopy);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] DeleteScriptFile error: {ex.Message}");
            }
        }

        // ── Helpers ──

        private static string EnsureLuaExtension(string name)
        {
            return name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? name : name + ".lua";
        }

        private static void CleanupStaleFiles(string directory, System.Collections.Generic.HashSet<string> validFiles)
        {
            try
            {
                foreach (var file in Directory.GetFiles(directory, "*.lua"))
                {
                    if (!validFiles.Contains(Path.GetFileName(file)))
                    {
                        try { File.Delete(file); }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ScriptManager] CleanupStaleFiles error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScriptManager] CleanupStaleFiles dir error: {ex.Message}");
            }
        }
    }
}
