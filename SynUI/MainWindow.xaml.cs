using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SynUI.API;
using SynUI.Models;
using SynUI.Services;
using SynUI.Views;

namespace SynUI
{
    public partial class MainWindow : Window
    {
        // ── Services ─────────────────────────────────────────────────────────────
        private readonly TabManager      _tabManager      = new();
        private readonly EditorManager   _editorManager   = new();
        private readonly ExecutionService _executionService = new();
        private readonly SynapseZAPI2   _api2            = SynapseZAPI2.Instance;
        private bool _isUpdatingEditor;
        private Action<string>? _themeAppliedHandler;

        // ── Instance polling ─────────────────────────────────────────────────────
        private readonly DispatcherTimer _instanceTimer   = new() { Interval = TimeSpan.FromSeconds(3) };
        private int _selectedPid = 0;

        // ── Terminal auto-scroll ─────────────────────────────────────────────────
        private bool _terminalAutoScroll = true;

        // ────────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            SetupServices();
            LoadLuaSyntax();
            _tabManager.LoadState();
        }

        // ═══════════════════════════════════
        //  SERVICE WIRING
        // ═══════════════════════════════════

        private void SetupServices()
        {
            _tabManager.TabsChanged      += RebuildTabList;
            _tabManager.ActiveTabChanged += OnActiveTabChanged;
            _executionService.OnStatusUpdate += SetStatus;

            // Console output from Roblox via HTTP
            _api2.ConsoleOutput += OnConsoleOutput;

            // Instance polling
            _instanceTimer.Tick += (_, _) => PollInstances();
        }

        private void OnActiveTabChanged(ScriptTab tab)
        {
            _isUpdatingEditor = true;
            CodeEditor.Text = tab.Content;
            _isUpdatingEditor = false;
            _editorManager.SetActiveDocument(tab.Id);
            _editorManager.OpenLspDocument(tab.Id, tab.Content);
        }

        // ═══════════════════════════════════
        //  INITIALIZATION
        // ═══════════════════════════════════

        private void LoadLuaSyntax()
        {
            try
            {
                using var stream = Application.GetResourceStream(
                    new Uri("pack://application:,,,/Resources/Lua.xshd"))?.Stream;
                if (stream != null)
                {
                    using var reader = new XmlTextReader(stream);
                    CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SynUI] Syntax load error: {ex.Message}");
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SettingsWindow.LoadSavedTheme();

            // Wire AvalonEdit colors (initial + on every theme change)
            _themeAppliedHandler = _ => Dispatcher.Invoke(RefreshEditorColors);
            SettingsWindow.ThemeApplied += _themeAppliedHandler;
            RefreshEditorColors();

            CodeEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(50, 124, 58, 237));
            CodeEditor.TextArea.SelectionBorder = null;
            CodeEditor.TextArea.SelectionForeground = null;

            CodeEditor.TextArea.TextEntered  += (s, ev) => _editorManager.HandleTextEntered(CodeEditor.TextArea, ev.Text, k => FindResource(k) as Brush);
            CodeEditor.TextArea.TextEntering += (s, ev) => _editorManager.HandleTextEntering(ev);

            // Kick off LSP
            _ = LspManager.Instance.StartAsync();

            // Kill any stale process holding port 1338 (previous desktop instance)
            try
            {
                var psi = new ProcessStartInfo("cmd.exe",
                    $"/c for /f \"tokens=5\" %a in ('netstat -ano ^| findstr \":{SynapseZAPI2.Port} \"') do taskkill /F /PID %a 2>nul")
                {
                    CreateNoWindow = true, UseShellExecute = false
                };
                using var kill = Process.Start(psi);
                kill?.WaitForExit(1500);
            }
            catch { }

            // Start console server
            _api2.Start();
            AppendTerminalInfo($"[SynUI] Console server started on port {SynapseZAPI2.Port}");

            // Fade-in animation
            ((Storyboard)FindResource("FadeIn")).Begin();

            // Init instance selector
            PopulateInstanceSelector(new List<Process>());
            _instanceTimer.Start();
            PollInstances();

            await CheckWeaoStatusAsync();
        }

        // ═══════════════════════════════════
        //  EDITOR COLORS
        // ═══════════════════════════════════

        private void RefreshEditorColors()
        {
            var surface = FindResource("BgSurfaceBrush") as Brush ?? Brushes.Black;
            var purple  = FindResource("AccentPurpleBrush") as Brush ?? Brushes.Purple;
            var blue    = FindResource("AccentBlueBrush") as Brush ?? Brushes.Blue;

            // Set via SetResourceReference so DynamicResource binding is preserved for future changes
            CodeEditor.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgSurfaceBrush");
            CodeEditor.TextArea.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgSurfaceBrush");
            CodeEditor.TextArea.Caret.CaretBrush = purple;
            CodeEditor.TextArea.TextView.LinkTextForegroundBrush = blue;
            CodeEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255));
            CodeEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)), 1);
            CodeEditor.TextArea.TextView.Redraw();
        }

        // ═══════════════════════════════════
        //  TAB UI
        // ═══════════════════════════════════

        private void RebuildTabList()
        {
            TabList.Children.Clear();
            AutoExecTabList.Children.Clear();

            foreach (var tab in _tabManager.Tabs)
            {
                var panel = CreateTabPanel(tab);
                if (tab.IsAutoExec) AutoExecTabList.Children.Add(panel);
                else                TabList.Children.Add(panel);
            }

            ScriptCountText.Text = _tabManager.TabCount == 1
                ? "1 script" : $"{_tabManager.TabCount} scripts";

            // If no tabs, ALWAYS show overlay
            if (_tabManager.TabCount == 0)
                WelcomeOverlay.Visibility = Visibility.Visible;

            // Update Continue button visibility based on tab count
            if (ContinueBtn != null)
                ContinueBtn.Visibility = _tabManager.TabCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Grid CreateTabPanel(ScriptTab tab)
        {
            bool isActive = tab.Id == _tabManager.ActiveTabId;

            var grid = new Grid { Tag = tab.Id };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btn = new Button
            {
                Content = tab.Name,
                Style   = (Style)FindResource(isActive ? "ScriptBtnActive" : "ScriptBtn"),
                Tag     = tab.Id,
            };
            btn.Click             += (_, _) => { _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text); _tabManager.SwitchToTab(tab.Id); WelcomeOverlay.Visibility = Visibility.Collapsed; RebuildTabList(); };
            btn.MouseRightButtonUp += TabButton_RightClick;
            Grid.SetColumn(btn, 0);

            var close = new Button
            {
                Style   = (Style)FindResource("CloseTabBtn"),
                Tag     = tab.Id,
                Margin  = new Thickness(0, 0, 4, 0),
                Visibility = Visibility.Hidden,
            };
            close.Click += (_, _) => _tabManager.CloseTab(tab.Id);
            Grid.SetColumn(close, 1);

            grid.Background  = Brushes.Transparent;
            grid.MouseEnter += (_, _) => { if (_tabManager.TabCount > 1) close.Visibility = Visibility.Visible; };
            grid.MouseLeave += (_, _) => { if (tab.Id != _tabManager.ActiveTabId) close.Visibility = Visibility.Hidden; };
            if (isActive && _tabManager.TabCount > 1) close.Visibility = Visibility.Visible;

            grid.Children.Add(btn);
            grid.Children.Add(close);
            return grid;
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
            _tabManager.AddTab(isAutoExec: false);
            var id = _tabManager.GetLatestTabId();
            if (id != null) ShowRenameDialog(id);
        }

        private void AddAutoExecTab_Click(object sender, RoutedEventArgs e)
        {
            _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
            _tabManager.AddTab(isAutoExec: true);
            var id = _tabManager.GetLatestTabId();
            if (id != null) ShowRenameDialog(id);
        }

        // ═══════════════════════════════════
        //  CONTEXT MENU
        // ═══════════════════════════════════

        private void TabButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string tabId) return;
            var tab = _tabManager.GetTab(tabId);
            if (tab == null) return;

            var menu = new ContextMenu();

            var toggleAE = new MenuItem { Header = tab.IsAutoExec ? "Remove from Auto Exec" : "Add to Auto Exec" };
            toggleAE.Click += (_, _) => _tabManager.ToggleAutoExec(tabId);
            menu.Items.Add(toggleAE);
            menu.Items.Add(new Separator());

            var rename = new MenuItem { Header = "Rename" };
            rename.Click += (_, _) => ShowRenameDialog(tabId);
            menu.Items.Add(rename);

            var dup = new MenuItem { Header = "Duplicate" };
            dup.Click += (_, _) => _tabManager.DuplicateTab(tabId, () => CodeEditor.Text);
            menu.Items.Add(dup);
            menu.Items.Add(new Separator());

            var del = new MenuItem
            {
                Header = "Delete",
                Foreground = FindResource("AccentRedBrush") as Brush,
                IsEnabled = _tabManager.TabCount > 1,
            };
            del.Click += (_, _) => _tabManager.CloseTab(tabId);
            menu.Items.Add(del);

            btn.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void ShowRenameDialog(string tabId)
        {
            var tab = _tabManager.GetTab(tabId);
            if (tab == null) return;

            var dialog = new Window
            {
                Title = "Rename",
                Width = 320, Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background   = FindResource("BgElevatedBrush") as Brush,
                BorderBrush  = FindResource("BorderBrush") as Brush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18),
            };
            border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 };

            var stack = new StackPanel();
            var label = new TextBlock
            {
                Text       = "Script Name",
                FontSize   = 10.5,
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas"),
                Foreground = FindResource("TextMutedBrush") as Brush,
                Margin     = new Thickness(0, 0, 0, 6),
            };
            var textBox = new TextBox
            {
                Text            = tab.Name,
                FontSize        = 13,
                FontFamily      = new FontFamily("JetBrains Mono, Cascadia Code, Consolas"),
                Padding         = new Thickness(9, 6, 9, 6),
                Background      = FindResource("BgSurfaceBrush") as Brush,
                Foreground      = FindResource("TextPrimaryBrush") as Brush,
                BorderBrush     = FindResource("BorderBrush") as Brush,
                CaretBrush      = FindResource("TextPrimaryBrush") as Brush,
                SelectionBrush  = FindResource("AccentPurpleBrush") as Brush,
                BorderThickness = new Thickness(1),
            };
            textBox.SelectAll();

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancelBtn = new Button
            {
                Content     = "Cancel",
                Padding     = new Thickness(14, 5, 14, 5),
                Background  = FindResource("BgHoverBrush") as Brush,
                Foreground  = FindResource("TextSecondaryBrush") as Brush,
                BorderThickness = new Thickness(0),
                FontFamily  = new FontFamily("JetBrains Mono, Cascadia Code, Consolas"),
                FontSize    = 11.5,
                Cursor      = Cursors.Hand,
                Margin      = new Thickness(0, 0, 8, 0),
            };
            cancelBtn.Click += (_, _) => dialog.Close();

            var saveBtn = new Button
            {
                Content    = "Save",
                Padding    = new Thickness(14, 5, 14, 5),
                Background = FindResource("AccentPurpleBrush") as Brush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("JetBrains Mono, Cascadia Code, Consolas"),
                FontSize   = 11.5,
                FontWeight = FontWeights.SemiBold,
                Cursor     = Cursors.Hand,
            };
            saveBtn.Click += (_, _) =>
            {
                var name = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(name)) _tabManager.RenameTab(tabId, name);
                dialog.Close();
            };

            textBox.KeyDown += (_, ev) =>
            {
                if (ev.Key == Key.Enter)  saveBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (ev.Key == Key.Escape) dialog.Close();
            };

            btns.Children.Add(cancelBtn);
            btns.Children.Add(saveBtn);
            stack.Children.Add(label);
            stack.Children.Add(textBox);
            stack.Children.Add(btns);
            border.Child = stack;
            dialog.Content = border;
            dialog.Loaded += (_, _) => textBox.Focus();
            dialog.ShowDialog();
        }

        // ═══════════════════════════════════
        //  EXECUTION
        // ═══════════════════════════════════

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            _executionService.Execute(CodeEditor.Text, _selectedPid);
        }

        private void SetStatus(string text, StatusType type)
        {
            Dispatcher.Invoke(() =>
            {
                ExecSeparator.Visibility = Visibility.Visible;
                StatusDot.Visibility     = Visibility.Visible;

                StatusText.Text = text;

                Color dotColor = type switch
                {
                    StatusType.Success => (Color)FindResource("AccentGreen"),
                    StatusType.Warning => (Color)FindResource("AccentOrange"),
                    StatusType.Error   => (Color)FindResource("AccentRed"),
                    _                  => (Color)FindResource("AccentBlue"),
                };
                Color textColor = type switch
                {
                    StatusType.Success => (Color)FindResource("AccentGreen"),
                    StatusType.Error   => (Color)FindResource("AccentRed"),
                    _                  => (Color)FindResource("TextSecondary"),
                };

                StatusDot.Fill       = new SolidColorBrush(dotColor);
                StatusText.Foreground = new SolidColorBrush(textColor);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    ExecSeparator.Visibility = Visibility.Collapsed;
                    StatusDot.Visibility     = Visibility.Collapsed;
                    StatusText.Text = "";
                };
                timer.Start();
            });
        }

        // ═══════════════════════════════════
        //  TERMINAL
        // ═══════════════════════════════════

        private void HookConsole_Click(object sender, RoutedEventArgs e)
        {
            var token     = _api2.NewHookToken();
            var hookScript = SynapseZAPI2.GetHookScript(SynapseZAPI2.Port, token);
            _executionService.Execute(hookScript, _selectedPid);
            AppendTerminalInfo($"[SynUI] Injecting console hook (PID {(_selectedPid == 0 ? "all" : _selectedPid.ToString())})…");
        }

        private void ClearTerminal_Click(object sender, RoutedEventArgs e)
        {
            TerminalBox.Document.Blocks.Clear();
        }

        private void OnConsoleOutput(int type, string text)
        {
            Dispatcher.Invoke(() => AppendTerminalLine(type, text));
        }

        private void AppendTerminalLine(int type, string text)
        {
            var ts    = DateTime.Now.ToString("HH:mm:ss");
            var label = new[] { "OUT", "INF", "WRN", "ERR" }[Math.Clamp(type, 0, 3)];

            var tsColor = (Brush)FindResource("TextMutedBrush");
            Brush tagColor = type switch
            {
                1 => new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),  // blue
                2 => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),  // amber
                3 => new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),  // red
                _ => (Brush)FindResource("TextMutedBrush"),
            };
            Brush textColor = type switch
            {
                1 => new SolidColorBrush(Color.FromArgb(220, 0x60, 0xA5, 0xFA)),
                2 => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
                3 => new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
                _ => (Brush)FindResource("TextSecondaryBrush"),
            };

            var para = new Paragraph
            {
                Margin  = new Thickness(0),
                Padding = new Thickness(0),
                LineHeight = 18,
            };

            if (type == 2 || type == 3)
                para.Background = type == 3
                    ? new SolidColorBrush(Color.FromArgb(18, 0xEF, 0x44, 0x44))
                    : new SolidColorBrush(Color.FromArgb(10, 0xF5, 0x9E, 0x0B));

            para.Inlines.Add(new Run($"  {ts}  ") { Foreground = tsColor });
            para.Inlines.Add(new Run($"{label}  ") { Foreground = tagColor, FontWeight = FontWeights.Bold });
            para.Inlines.Add(new Run(text) { Foreground = textColor });

            TerminalBox.Document.Blocks.Add(para);

            if (_terminalAutoScroll)
                TerminalBox.ScrollToEnd();

            // Keep memory bounded
            while (TerminalBox.Document.Blocks.Count > 1500)
                TerminalBox.Document.Blocks.Remove(TerminalBox.Document.Blocks.FirstBlock);
        }

        private void AppendTerminalInfo(string text)
        {
            Dispatcher.Invoke(() => AppendTerminalLine(1, text));
        }

        // ═══════════════════════════════════
        //  INSTANCE SELECTOR
        // ═══════════════════════════════════

        private void PollInstances()
        {
            try
            {
                var instances = SynapseZAPI.GetSynzRobloxInstances();
                PopulateInstanceSelector(instances);
            }
            catch { }
        }

        private void PopulateInstanceSelector(List<Process> instances)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => PopulateInstanceSelector(instances));
                return;
            }

            var prevPid = _selectedPid;
            InstanceSelector.Items.Clear();
            InstanceSelector.Items.Add("All Instances");

            foreach (var p in instances)
                InstanceSelector.Items.Add($"PID {p.Id}");

            // Try to keep the same selection
            if (prevPid == 0 || instances.All(p => p.Id != prevPid))
            {
                InstanceSelector.SelectedIndex = 0;
                _selectedPid = 0;
            }
            else
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    if (instances[i].Id == prevPid)
                    {
                        InstanceSelector.SelectedIndex = i + 1;
                        break;
                    }
                }
            }
        }

        private void InstanceSelector_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (InstanceSelector.SelectedIndex <= 0)
            {
                _selectedPid = 0;
                return;
            }
            var item = InstanceSelector.SelectedItem?.ToString();
            if (item != null && item.StartsWith("PID ") && int.TryParse(item.Substring(4), out int pid))
                _selectedPid = pid;
        }

        // ═══════════════════════════════════
        //  EDITOR EVENTS
        // ═══════════════════════════════════

        private void CodeEditor_TextChanged(object sender, EventArgs e)
        {
            if (!_isUpdatingEditor)
            {
                _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
                _editorManager.HandleTextChanged(CodeEditor.TextArea, CodeEditor.Text);
            }
        }

        // ═══════════════════════════════════
        //  WEAO STATUS
        // ═══════════════════════════════════

        private async Task CheckWeaoStatusAsync()
        {
            var status = await WeaoService.GetSynapseZStatusAsync();
            if (status.Success)
            {
                RobloxVersionText.Text = $"rbx {status.RobloxVersion}";
                if (status.IsUpdated)
                {
                    WeaoStatusText.Text     = "SZ: Functional";
                    WeaoStatusText.Foreground = FindResource("AccentGreenBrush") as Brush;
                    AnimateWeaoDot((Color)FindResource("AccentGreen"));
                }
                else
                {
                    WeaoStatusText.Text     = "SZ: Outdated";
                    WeaoStatusText.Foreground = FindResource("AccentRedBrush") as Brush;
                    AnimateWeaoDot((Color)FindResource("AccentRed"));
                }
            }
            else
            {
                WeaoStatusText.Text     = "SZ: Unknown";
                WeaoStatusText.Foreground = FindResource("AccentOrangeBrush") as Brush;
                RobloxVersionText.Text  = "rbx ???";
            }
        }

        private void AnimateWeaoDot(Color c)
        {
            var brush = new SolidColorBrush(c);
            WeaoStatusDot.Fill = brush;
            var anim = new ColorAnimation
            {
                From = c,
                To   = Color.FromArgb(55, c.R, c.G, c.B),
                Duration = TimeSpan.FromSeconds(1.2),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        // ═══════════════════════════════════
        //  WINDOW LIFECYCLE & CHROME
        // ═══════════════════════════════════

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Enter) && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                _executionService.Execute(CodeEditor.Text, _selectedPid);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _instanceTimer.Stop();
            _api2.ConsoleOutput -= OnConsoleOutput;
            if (_themeAppliedHandler != null)
                SettingsWindow.ThemeApplied -= _themeAppliedHandler;
            _api2.Stop();
            _tabManager.SaveState(() => CodeEditor.Text);
            ScriptManager.FlushNow();
            LspManager.Instance.Dispose();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.Margin = new Thickness(6);
                MaximizeBtn.Content = "\uE923";
            }
            else
            {
                RootBorder.CornerRadius = new CornerRadius(10);
                RootBorder.Margin = new Thickness(0);
                MaximizeBtn.Content = "\uE739";
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) Maximize_Click(sender, e);
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)  => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e)  => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e)     => Close();

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow { Owner = this };
            win.ShowDialog();
        }

        private void ContinueToEditor_Click(object sender, RoutedEventArgs e)
            => WelcomeOverlay.Visibility = Visibility.Collapsed;

        private void WelcomeNewScript_Click(object sender, RoutedEventArgs e)
        {
            _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
            _tabManager.AddTab(isAutoExec: false);
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            var id = _tabManager.GetLatestTabId();
            if (id != null) ShowRenameDialog(id);
        }

        private void WelcomeNewAutoExec_Click(object sender, RoutedEventArgs e)
        {
            _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
            _tabManager.AddTab(isAutoExec: true);
            WelcomeOverlay.Visibility = Visibility.Collapsed;
            var id = _tabManager.GetLatestTabId();
            if (id != null) ShowRenameDialog(id);
        }
    }
}
