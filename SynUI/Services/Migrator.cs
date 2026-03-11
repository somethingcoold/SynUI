using System;
using System.IO;

namespace SynUI.Services
{
    /// <summary>
    /// Handles one-time migration from legacy data locations to the new
    /// %LOCALAPPDATA%\SynUI\ canonical location.
    /// </summary>
    public static class Migrator
    {
        private static readonly string MigrationMarker = Path.Combine(AppPaths.DataRoot, ".migrated");

        /// <summary>
        /// Runs all migration paths. Safe to call multiple times — uses a marker file
        /// to skip if already migrated.
        /// </summary>
        public static void MigrateAll()
        {
            if (File.Exists(MigrationMarker)) return;

            try
            {
                // Migration 1: Old %APPDATA%\SynUI → new location
                string oldAppData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SynUI");
                MigrateFromDirectory(oldAppData);

                // Migration 2: Old Workspace/ folder (alongside binary) → new location
                string? oldWorkspace = FindLegacyWorkspace();
                if (oldWorkspace != null)
                {
                    MigrateFromDirectory(oldWorkspace);
                }

                // Write marker so we don't re-migrate
                File.WriteAllText(MigrationMarker, $"Migrated at {DateTime.UtcNow:O}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Migrator] Migration error: {ex.Message}");
            }
        }

        /// <summary>
        /// Migrates state.json and .lua files from a legacy directory into the new AppPaths locations.
        /// </summary>
        private static void MigrateFromDirectory(string sourcePath)
        {
            if (!Directory.Exists(sourcePath)) return;

            try
            {
                // Migrate state.json
                string oldState = Path.Combine(sourcePath, "state.json");
                if (File.Exists(oldState) && !File.Exists(AppPaths.StateFilePath))
                {
                    File.Copy(oldState, AppPaths.StateFilePath);
                    System.Diagnostics.Debug.WriteLine($"[Migrator] Migrated state.json from {sourcePath}");
                }

                // Migrate .lua files from root of source
                MigrateLuaFiles(sourcePath, AppPaths.ScriptsDir);

                // Migrate .lua files from scripts/ subfolder
                string scriptsSubDir = Path.Combine(sourcePath, "scripts");
                MigrateLuaFiles(scriptsSubDir, AppPaths.ScriptsDir);

                // Migrate .lua files from autoexec/ subfolder
                string autoExecSubDir = Path.Combine(sourcePath, "autoexec");
                MigrateLuaFiles(autoExecSubDir, AppPaths.AutoExecDir);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Migrator] Error migrating from {sourcePath}: {ex.Message}");
            }
        }

        private static void MigrateLuaFiles(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir)) return;

            foreach (var file in Directory.GetFiles(sourceDir, "*.lua"))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                {
                    try
                    {
                        File.Copy(file, dest);
                        System.Diagnostics.Debug.WriteLine($"[Migrator] Migrated {Path.GetFileName(file)} → {destDir}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Migrator] Failed to copy {file}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Walks upward from the binary directory to find the old Workspace/ folder.
        /// </summary>
        private static string? FindLegacyWorkspace()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                string check = Path.Combine(current, "Workspace");
                if (Directory.Exists(check))
                    return check;

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }
            return null;
        }
    }
}
