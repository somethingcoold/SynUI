using System;
using System.IO;

namespace SynUI.Services
{
    /// <summary>
    /// Single source of truth for all application and executor data paths.
    /// All directories are created on first access.
    /// </summary>
    public static class AppPaths
    {
        // ── UI-Owned Data (%LOCALAPPDATA%\SynUI\) ──

        /// <summary>Root data directory: %LOCALAPPDATA%\SynUI</summary>
        public static readonly string DataRoot;

        /// <summary>Regular .lua script files: %LOCALAPPDATA%\SynUI\scripts\</summary>
        public static readonly string ScriptsDir;

        /// <summary>UI's copy of autoexec scripts: %LOCALAPPDATA%\SynUI\autoexec\</summary>
        public static readonly string AutoExecDir;

        /// <summary>Tab/state persistence: %LOCALAPPDATA%\SynUI\state.json</summary>
        public static readonly string StateFilePath;

        // ── Synapse Z Executor Paths (%LOCALAPPDATA%\Synapse Z\) ──

        /// <summary>Synapse Z root: %LOCALAPPDATA%\Synapse Z\</summary>
        public static readonly string SynapseZRoot;

        /// <summary>Executor's autoexec folder (synced FROM SynUI): %LOCALAPPDATA%\Synapse Z\autoexec\</summary>
        public static readonly string SynapseZAutoExecDir;

        /// <summary>Synapse Z binaries: %LOCALAPPDATA%\Synapse Z\bin\</summary>
        public static readonly string SynapseZBinDir;

        /// <summary>Luau LSP binary directory: %LOCALAPPDATA%\Synapse Z\lsp\</summary>
        public static readonly string LspDir;

        /// <summary>Luau LSP executable path: %LOCALAPPDATA%\Synapse Z\lsp\luau-lsp.exe</summary>
        public static readonly string LspExePath;

        static AppPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // UI-owned
            DataRoot = Path.Combine(localAppData, "SynUI");
            ScriptsDir = Path.Combine(DataRoot, "scripts");
            AutoExecDir = Path.Combine(DataRoot, "autoexec");
            StateFilePath = Path.Combine(DataRoot, "state.json");

            // Synapse Z executor
            SynapseZRoot = Path.Combine(localAppData, "Synapse Z");
            SynapseZAutoExecDir = Path.Combine(SynapseZRoot, "autoexec");
            SynapseZBinDir = Path.Combine(SynapseZRoot, "bin");
            LspDir = Path.Combine(SynapseZRoot, "lsp");
            LspExePath = Path.Combine(LspDir, "luau-lsp.exe");

            // Ensure all directories exist
            EnsureDirectories();
        }

        /// <summary>
        /// Creates all required directories if they don't already exist.
        /// </summary>
        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ScriptsDir);
            Directory.CreateDirectory(AutoExecDir);
            Directory.CreateDirectory(SynapseZAutoExecDir);
        }
    }
}
