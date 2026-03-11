using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SynUI.Models;
using SynUI.Services;

namespace SynUI
{
    public partial class MainWindow : Window
    {
        private readonly TabManager _tabManager = new();
        private readonly EditorManager _editorManager = new();
        private readonly ExecutionService _executionService = new();
        private bool _isUpdatingEditor = false;

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
            // Tab manager events
            _tabManager.TabsChanged += RebuildTabList;
            _tabManager.ActiveTabChanged += OnActiveTabChanged;

            // Execution service events
            _executionService.OnStatusUpdate += SetStatus;
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
                    var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                    CodeEditor.SyntaxHighlighting = highlighting;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SynUI] Syntax load error: {ex.Message}");
            }
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
                TabList.Children.Add(CreateTabPanel(tab));

                if (tab.IsAutoExec)
                    AutoExecTabList.Children.Add(CreateTabPanel(tab));
            }

            ScriptCountText.Text = _tabManager.TabCount == 1
                ? "1 script"
                : $"{_tabManager.TabCount} scripts";
        }

        private Grid CreateTabPanel(ScriptTab tab)
        {
            var grid = new Grid { Tag = tab.Id };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bool isActive = tab.Id == _tabManager.ActiveTabId;

            var btn = new Button
            {
                Content = tab.Name,
                Style = (Style)FindResource(isActive ? "TabButtonActive" : "TabButton"),
                Tag = tab.Id
            };
            btn.Click += (s, e) =>
            {
                _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
                _tabManager.SwitchToTab(tab.Id);
                RebuildTabList();
            };
            btn.MouseRightButtonUp += TabButton_RightClick;
            Grid.SetColumn(btn, 0);

            var closeBtn = new Button
            {
                Style = (Style)FindResource("TabCloseBtn"),
                Tag = tab.Id,
                Margin = new Thickness(0, 0, 6, 0),
                Visibility = Visibility.Hidden
            };
            closeBtn.Click += (s, e) => _tabManager.CloseTab(tab.Id);
            Grid.SetColumn(closeBtn, 1);

            // Make the grid catch all mouse events seamlessly across its area
            grid.Background = Brushes.Transparent;
            
            grid.MouseEnter += (s, e) => 
            {
                if (_tabManager.TabCount > 1) 
                    closeBtn.Visibility = Visibility.Visible;
            };
            
            grid.MouseLeave += (s, e) =>
            {
                if (tab.Id != _tabManager.ActiveTabId)
                    closeBtn.Visibility = Visibility.Hidden;
            };

            if (isActive)
                closeBtn.Visibility = _tabManager.TabCount > 1 ? Visibility.Visible : Visibility.Hidden;

            grid.Children.Add(btn);
            grid.Children.Add(closeBtn);

            return grid;
        }

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
            _tabManager.AddTab(isAutoExec: false);
            string? id = _tabManager.GetLatestTabId();
            if (id != null) ShowRenameDialog(id);
        }

        private void AddAutoExecTab_Click(object sender, RoutedEventArgs e)
        {
            _tabManager.SyncEditorToActiveTab(() => CodeEditor.Text);
            _tabManager.AddTab(isAutoExec: true);
            string? id = _tabManager.GetLatestTabId();
            if (id != null) ShowRenameDialog(id);
        }

        // ═══════════════════════════════════
        //  CONTEXT MENU
        // ═══════════════════════════════════

        private void TabButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tabId)
            {
                var tab = _tabManager.GetTab(tabId);
                if (tab == null) return;

                var menu = new ContextMenu();

                var toggleAutoExecItem = new MenuItem
                {
                    Header = tab.IsAutoExec ? "Remove from Auto Exec" : "Add to Auto Exec"
                };
                toggleAutoExecItem.Click += (s, ev) => _tabManager.ToggleAutoExec(tabId);
                menu.Items.Add(toggleAutoExecItem);

                menu.Items.Add(new Separator { Background = FindResource("BorderBrush") as Brush });

                var renameItem = new MenuItem { Header = "Rename" };
                renameItem.Click += (s, _) => ShowRenameDialog(tabId);
                menu.Items.Add(renameItem);

                var duplicateItem = new MenuItem { Header = "Duplicate" };
                duplicateItem.Click += (s, _) => _tabManager.DuplicateTab(tabId, () => CodeEditor.Text);
                menu.Items.Add(duplicateItem);

                menu.Items.Add(new Separator { Background = FindResource("BorderBrush") as Brush });

                var deleteItem = new MenuItem
                {
                    Header = "Delete",
                    Foreground = FindResource("AccentRedBrush") as Brush,
                    IsEnabled = _tabManager.TabCount > 1
                };
                deleteItem.Click += (s, _) => _tabManager.CloseTab(tabId);
                menu.Items.Add(deleteItem);

                menu.IsOpen = true;
                btn.ContextMenu = menu;
                e.Handled = true;
            }
        }

        private void ShowRenameDialog(string tabId)
        {
            var tab = _tabManager.GetTab(tabId);
            if (tab == null) return;

            var dialog = new Window
            {
                Title = "Rename Script",
                Width = 340,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = FindResource("BgElevatedBrush") as Brush,
                BorderBrush = FindResource("BorderBrush") as Brush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20)
            };

            var stack = new StackPanel();

            var label = new TextBlock
            {
                Text = "Script Name",
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                FontFamily = new FontFamily("Segoe UI")
            };

            var textBox = new TextBox
            {
                Text = tab.Name,
                FontSize = 13,
                Padding = new Thickness(10, 7, 10, 7),
                Background = FindResource("BgSurfaceBrush") as Brush,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                BorderBrush = FindResource("BorderBrush") as Brush,
                BorderThickness = new Thickness(1),
                CaretBrush = FindResource("TextPrimaryBrush") as Brush,
                FontFamily = new FontFamily("Segoe UI"),
                SelectionBrush = FindResource("AccentPurpleBrush") as Brush
            };
            textBox.SelectAll();

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(16, 6, 16, 6),
                Background = FindResource("BgHoverBrush") as Brush,
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelBtn.Click += (s, e) => dialog.Close();

            var saveBtn = new Button
            {
                Content = "Save",
                Padding = new Thickness(16, 6, 16, 6),
                Background = FindResource("AccentPurpleBrush") as Brush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI")
            };
            saveBtn.Click += (s, e) =>
            {
                string newName = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName))
                    _tabManager.RenameTab(tabId, newName);
                dialog.Close();
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) saveBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (e.Key == Key.Escape) dialog.Close();
            };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(saveBtn);

            stack.Children.Add(label);
            stack.Children.Add(textBox);
            stack.Children.Add(btnPanel);
            border.Child = stack;
            dialog.Content = border;

            dialog.Loaded += (s, e) => { textBox.Focus(); };
            dialog.ShowDialog();
        }

        // ═══════════════════════════════════
        //  EXECUTION
        // ═══════════════════════════════════

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            _executionService.Execute(CodeEditor.Text);
        }

        private void SetStatus(string text, StatusType type)
        {
            Dispatcher.Invoke(() =>
            {
                ExecSeparator.Visibility = Visibility.Visible;
                StatusDot.Visibility = Visibility.Visible;
                StatusText.Visibility = Visibility.Visible;

                StatusText.Text = text;

                Color dotColor = type switch
                {
                    StatusType.Success => (Color)FindResource("AccentGreen"),
                    StatusType.Warning => (Color)FindResource("AccentOrange"),
                    StatusType.Error => (Color)FindResource("AccentRed"),
                    _ => (Color)FindResource("AccentBlue")
                };

                Color textColor = type switch
                {
                    StatusType.Success => (Color)FindResource("AccentGreen"),
                    StatusType.Error => (Color)FindResource("AccentRed"),
                    _ => (Color)FindResource("TextSecondary")
                };

                StatusDot.Fill = new SolidColorBrush(dotColor);
                StatusText.Foreground = new SolidColorBrush(textColor);

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(4)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    ExecSeparator.Visibility = Visibility.Collapsed;
                    StatusDot.Visibility = Visibility.Collapsed;
                    StatusText.Text = "";
                };
                timer.Start();
            });
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

        private void CodeEditor_TextEntered(object sender, TextCompositionEventArgs e)
        {
            _editorManager.HandleTextEntered(
                CodeEditor.TextArea,
                e.Text,
                key => FindResource(key) as Brush);
        }

        private void CodeEditor_TextEntering(object sender, TextCompositionEventArgs e)
        {
            _editorManager.HandleTextEntering(e);
        }

        // ═══════════════════════════════════
        //  WINDOW LIFECYCLE & CHROME
        // ═══════════════════════════════════

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Dark-theme the AvalonEdit control
            var surfaceBrush = FindResource("BgSurfaceBrush") as Brush;
            var accentBrush = FindResource("AccentPurpleBrush") as Brush;

            CodeEditor.TextArea.TextView.BackgroundRenderers.Clear();
            CodeEditor.Background = surfaceBrush;
            CodeEditor.TextArea.Background = surfaceBrush;
            CodeEditor.TextArea.Caret.CaretBrush = accentBrush;
            CodeEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(50, 124, 92, 252));
            CodeEditor.TextArea.SelectionBorder = null;
            CodeEditor.TextArea.SelectionForeground = null;
            CodeEditor.TextArea.TextView.LinkTextForegroundBrush = FindResource("AccentBlueBrush") as Brush;
            CodeEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
            CodeEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(8, 255, 255, 255)), 1);

            // Wire up autocomplete events
            CodeEditor.TextArea.TextEntered += CodeEditor_TextEntered;
            CodeEditor.TextArea.TextEntering += CodeEditor_TextEntering;

            // Entrance animation
            var anim = (Storyboard)FindResource("WindowFadeIn");
            anim.Begin();

            // LSP
            _ = LspManager.Instance.StartAsync();

            await CheckWeaoStatusAsync();
        }

        private async Task CheckWeaoStatusAsync()
        {
            var status = await WeaoService.GetSynapseZStatusAsync();
            if (status.Success)
            {
                RobloxVersionText.Text = $"Roblox version: {status.RobloxVersion}";

                if (status.IsUpdated)
                {
                    WeaoStatusText.Text = "Functional";
                    WeaoStatusText.Foreground = FindResource("AccentGreenBrush") as Brush;
                    WeaoStatusDot.Fill = FindResource("AccentGreenBrush") as Brush;
                    AnimateWeaoDot((Color)FindResource("AccentGreen"));
                }
                else
                {
                    WeaoStatusText.Text = "Down";
                    WeaoStatusText.Foreground = FindResource("AccentRedBrush") as Brush;
                    WeaoStatusDot.Fill = FindResource("AccentRedBrush") as Brush;
                    AnimateWeaoDot((Color)FindResource("AccentRed"));
                }
            }
            else
            {
                WeaoStatusText.Text = "API Error";
                WeaoStatusText.Foreground = FindResource("AccentOrangeBrush") as Brush;
                WeaoStatusDot.Fill = FindResource("AccentOrangeBrush") as Brush;
                RobloxVersionText.Text = "Roblox version: Unknown";
            }
        }

        private void AnimateWeaoDot(Color baseColor)
        {
            var anim = new ColorAnimation
            {
                From = baseColor,
                To = Color.FromArgb(60, baseColor.R, baseColor.G, baseColor.B),
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            var brush = new SolidColorBrush(baseColor);
            WeaoStatusDot.Fill = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _tabManager.SaveState(() => CodeEditor.Text);
            ScriptManager.FlushNow();
            LspManager.Instance.Dispose();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                MainBorder.CornerRadius = new CornerRadius(0);
                MainBorder.Margin = new Thickness(6);
                MaximizeBtn.Content = "\uE923";
            }
            else
            {
                MainBorder.CornerRadius = new CornerRadius(12);
                MainBorder.Margin = new Thickness(0);
                MaximizeBtn.Content = "\uE739";
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                Maximize_Click(sender, e);
            else
                DragMove();
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}