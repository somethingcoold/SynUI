using System;
using System.IO;
using System.Reflection;

namespace SynUI.Services
{
    public static class AppPaths
    {
        public static readonly string DataRoot;
        public static readonly string ScriptsDir;
        public static readonly string AutoExecDir;
        public static readonly string StateFilePath;
        public static readonly string WebAppDir;

        public static readonly string SynapseZRoot;
        public static readonly string SynapseZAutoExecDir;
        public static readonly string SynapseZBinDir;
        public static readonly string LspDir;
        public static readonly string LspExePath;

        static AppPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DataRoot = Path.Combine(localAppData, "SynUI");
            ScriptsDir = Path.Combine(DataRoot, "scripts");
            AutoExecDir = Path.Combine(DataRoot, "autoexec");
            StateFilePath = Path.Combine(DataRoot, "state.json");
            WebAppDir = Path.Combine(DataRoot, "WebApp");

            SynapseZRoot = Path.Combine(localAppData, "Synapse Z");
            SynapseZAutoExecDir = Path.Combine(SynapseZRoot, "autoexec");
            SynapseZBinDir = Path.Combine(SynapseZRoot, "bin");
            LspDir = Path.Combine(SynapseZRoot, "lsp");
            LspExePath = Path.Combine(LspDir, "luau-lsp.exe");

            EnsureDirectories();
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ScriptsDir);
            Directory.CreateDirectory(AutoExecDir);
            Directory.CreateDirectory(SynapseZAutoExecDir);
            Directory.CreateDirectory(WebAppDir);
        }

        public static void ExtractWebAppResources()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string prefix = "SynUI.Resources.WebApp.";
            
            foreach (var resName in assembly.GetManifestResourceNames())
            {
                if (!resName.StartsWith(prefix)) continue;

                string relativeName = resName.Substring(prefix.Length);
                string filePath = relativeName;

                if (filePath.StartsWith("dist.")) {
                    if (filePath.StartsWith("dist.assets."))
                        filePath = Path.Combine("dist", "assets", filePath.Substring(12));
                    else
                        filePath = Path.Combine("dist", filePath.Substring(5));
                }
                
                string targetPath = Path.Combine(WebAppDir, filePath.Replace("/", "\\"));
                string? targetFolder = Path.GetDirectoryName(targetPath);
                if (targetFolder != null) Directory.CreateDirectory(targetFolder);

                try {
                    using Stream? stream = assembly.GetManifestResourceStream(resName);
                    if (stream != null) {
                        using FileStream fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                        stream.CopyTo(fileStream);
                    }
                } catch { }
            }
        }
    }
}
