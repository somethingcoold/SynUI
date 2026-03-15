using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SynUI.Services;

namespace SynUI.Views
{
    public partial class SettingsWindow : Window
    {
        private static string _currentTheme = "dark";

        // ── Theme color maps ─────────────────────────────────────────────────────
        private static readonly Dictionary<string, Dictionary<string, Color>> Themes = new()
        {
            ["dark"] = new()
            {
                ["BgDeep"]          = ParseColor("#09090B"),
                ["BgBase"]          = ParseColor("#0D0D10"),
                ["BgSurface"]       = ParseColor("#111115"),
                ["BgElevated"]      = ParseColor("#18181D"),
                ["BgHover"]         = ParseColor("#1C1C22"),
                ["BgActive"]        = ParseColor("#22222A"),
                ["TextPrimary"]     = ParseColor("#E4E4E7"),
                ["TextSecondary"]   = ParseColor("#A1A1AA"),
                ["TextMuted"]       = ParseColor("#52525B"),
                ["AccentPurple"]    = ParseColor("#7C3AED"),
                ["AccentPurpleHover"]= ParseColor("#8B5CF6"),
                ["AccentBlue"]      = ParseColor("#3B82F6"),
                ["AccentGreen"]     = ParseColor("#10B981"),
                ["AccentRed"]       = ParseColor("#EF4444"),
                ["AccentOrange"]    = ParseColor("#F59E0B"),
                ["Border"]          = ParseColor("#27272F"),
                ["BorderSubtle"]    = ParseColor("#1E1E26"),
            },
            ["gray"] = new()
            {
                ["BgDeep"]          = ParseColor("#111113"),
                ["BgBase"]          = ParseColor("#18181B"),
                ["BgSurface"]       = ParseColor("#1C1C1F"),
                ["BgElevated"]      = ParseColor("#232327"),
                ["BgHover"]         = ParseColor("#27272A"),
                ["BgActive"]        = ParseColor("#2F2F33"),
                ["TextPrimary"]     = ParseColor("#FAFAFA"),
                ["TextSecondary"]   = ParseColor("#D4D4D8"),
                ["TextMuted"]       = ParseColor("#71717A"),
                ["AccentPurple"]    = ParseColor("#A1A1AA"),
                ["AccentPurpleHover"]= ParseColor("#D4D4D8"),
                ["AccentBlue"]      = ParseColor("#6366F1"),
                ["AccentGreen"]     = ParseColor("#22C55E"),
                ["AccentRed"]       = ParseColor("#EF4444"),
                ["AccentOrange"]    = ParseColor("#F59E0B"),
                ["Border"]          = ParseColor("#3F3F46"),
                ["BorderSubtle"]    = ParseColor("#27272A"),
            },
            ["blue"] = new()
            {
                ["BgDeep"]          = ParseColor("#0A1628"),
                ["BgBase"]          = ParseColor("#0D1B30"),
                ["BgSurface"]       = ParseColor("#112240"),
                ["BgElevated"]      = ParseColor("#152A50"),
                ["BgHover"]         = ParseColor("#1A3360"),
                ["BgActive"]        = ParseColor("#1E3C70"),
                ["TextPrimary"]     = ParseColor("#E2F0FF"),
                ["TextSecondary"]   = ParseColor("#93C5FD"),
                ["TextMuted"]       = ParseColor("#4E6E8E"),
                ["AccentPurple"]    = ParseColor("#60A5FA"),
                ["AccentPurpleHover"]= ParseColor("#93C5FD"),
                ["AccentBlue"]      = ParseColor("#34D399"),
                ["AccentGreen"]     = ParseColor("#10B981"),
                ["AccentRed"]       = ParseColor("#F87171"),
                ["AccentOrange"]    = ParseColor("#FBBF24"),
                ["Border"]          = ParseColor("#1E4080"),
                ["BorderSubtle"]    = ParseColor("#1E3A5F"),
            },
            ["bluedark"] = new()
            {
                ["BgDeep"]          = ParseColor("#080C14"),
                ["BgBase"]          = ParseColor("#0C1220"),
                ["BgSurface"]       = ParseColor("#10182A"),
                ["BgElevated"]      = ParseColor("#141E34"),
                ["BgHover"]         = ParseColor("#18243E"),
                ["BgActive"]        = ParseColor("#1C2A48"),
                ["TextPrimary"]     = ParseColor("#E2E8F0"),
                ["TextSecondary"]   = ParseColor("#94A3B8"),
                ["TextMuted"]       = ParseColor("#475569"),
                ["AccentPurple"]    = ParseColor("#3B82F6"),
                ["AccentPurpleHover"]= ParseColor("#60A5FA"),
                ["AccentBlue"]      = ParseColor("#8B5CF6"),
                ["AccentGreen"]     = ParseColor("#10B981"),
                ["AccentRed"]       = ParseColor("#EF4444"),
                ["AccentOrange"]    = ParseColor("#FBBF24"),
                ["Border"]          = ParseColor("#243650"),
                ["BorderSubtle"]    = ParseColor("#1E2D40"),
            },
        };

        // ── Brush key → resource key mapping ────────────────────────────────────
        private static readonly Dictionary<string, string> BrushKeys = new()
        {
            ["BgDeep"]           = "BgDeepBrush",
            ["BgBase"]           = "BgBaseBrush",
            ["BgSurface"]        = "BgSurfaceBrush",
            ["BgElevated"]       = "BgElevatedBrush",
            ["BgHover"]          = "BgHoverBrush",
            ["BgActive"]         = "BgActiveBrush",
            ["TextPrimary"]      = "TextPrimaryBrush",
            ["TextSecondary"]    = "TextSecondaryBrush",
            ["TextMuted"]        = "TextMutedBrush",
            ["AccentPurple"]     = "AccentPurpleBrush",
            ["AccentPurpleHover"]= "AccentPurpleHoverBrush",
            ["AccentBlue"]       = "AccentBlueBrush",
            ["AccentGreen"]      = "AccentGreenBrush",
            ["AccentRed"]        = "AccentRedBrush",
            ["AccentOrange"]     = "AccentOrangeBrush",
            ["Border"]           = "BorderBrush",
            ["BorderSubtle"]     = "BorderSubtleBrush",
        };

        public SettingsWindow()
        {
            InitializeComponent();
            UpdateCheckmarks(_currentTheme);
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Fired after a theme is applied — subscribers can refresh non-DynamicResource elements.</summary>
        public static event Action<string>? ThemeApplied;

        /// <summary>Applies the named theme globally and saves it to disk.</summary>
        public static void ApplyTheme(string name)
        {
            if (!Themes.TryGetValue(name, out var palette)) return;
            _currentTheme = name;

            foreach (var (key, color) in palette)
            {
                if (!BrushKeys.TryGetValue(key, out var brushKey)) continue;

                // 1. Mutate brush objects in merged dictionaries so StaticResource
                //    holders (which reference the same object) see the change immediately.
                foreach (var md in Application.Current.Resources.MergedDictionaries)
                {
                    if (md[brushKey] is SolidColorBrush existing)
                    {
                        if (!existing.IsFrozen)
                            existing.Color = color;
                    }
                }

                // 2. Replace top-level entry so DynamicResource users also update.
                Application.Current.Resources[brushKey] = new SolidColorBrush(color);
            }

            // Notify subscribers (e.g. MainWindow refreshes AvalonEdit colors)
            ThemeApplied?.Invoke(name);

            // Persist
            try
            {
                var settingsPath = Path.Combine(AppPaths.DataRoot, "settings.json");
                var json = JsonSerializer.Serialize(new { theme = name });
                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        /// <summary>Loads and applies the saved theme on startup.</summary>
        public static void LoadSavedTheme()
        {
            try
            {
                var settingsPath = Path.Combine(AppPaths.DataRoot, "settings.json");
                if (!File.Exists(settingsPath)) return;
                var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                if (doc.RootElement.TryGetProperty("theme", out var elem))
                    ApplyTheme(elem.GetString() ?? "dark");
            }
            catch { }
        }

        // ── UI handlers ──────────────────────────────────────────────────────────

        private void ThemeCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border card || card.Tag is not string themeName) return;
            ApplyTheme(themeName);
            UpdateCheckmarks(themeName);
        }

        private void UpdateCheckmarks(string active)
        {
            ThemeCheck_Dark.Visibility     = active == "dark"     ? Visibility.Visible : Visibility.Collapsed;
            ThemeCheck_Gray.Visibility     = active == "gray"     ? Visibility.Visible : Visibility.Collapsed;
            ThemeCheck_Blue.Visibility     = active == "blue"     ? Visibility.Visible : Visibility.Collapsed;
            ThemeCheck_BlueDark.Visibility = active == "bluedark" ? Visibility.Visible : Visibility.Collapsed;

            ThemeCard_Dark.BorderBrush     = active == "dark"     ? new SolidColorBrush(ParseColor("#7C3AED")) : new SolidColorBrush(ParseColor("#1E1E26"));
            ThemeCard_Gray.BorderBrush     = active == "gray"     ? new SolidColorBrush(ParseColor("#A1A1AA")) : new SolidColorBrush(ParseColor("#27272A"));
            ThemeCard_Blue.BorderBrush     = active == "blue"     ? new SolidColorBrush(ParseColor("#60A5FA")) : new SolidColorBrush(ParseColor("#1E3A5F"));
            ThemeCard_BlueDark.BorderBrush = active == "bluedark" ? new SolidColorBrush(ParseColor("#3B82F6")) : new SolidColorBrush(ParseColor("#1E2D40"));
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromRgb(
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16));
        }
    }
}
