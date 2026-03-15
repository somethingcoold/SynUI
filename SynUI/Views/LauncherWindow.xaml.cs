using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SynUI.Services;

namespace SynUI.Views
{
    public partial class LauncherWindow : Window
    {
        public LauncherWindow()
        {
            InitializeComponent();
            this.Loaded += (s, e) => {
                // Force ScriptManager to initialize paths and migrate files early
                ScriptManager.Initialize(); 
                
                var sb = (Storyboard)this.Resources["FadeIn"];
                sb.Begin();
            };
        }

        private async void Desktop_Click(object sender, RoutedEventArgs e)
        {
            await TransitionAndAction(() => {
                var main = new MainWindow();
                main.Show();
            });
        }

        private async void Web_Click(object sender, RoutedEventArgs e)
        {
            await TransitionAndAction(async () => {
                try
                {
                    AppPaths.ExtractWebAppResources();
                    string? webAppDir = FindWebAppDir();
                    if (webAppDir == null)
                    {
                        ShowErrorAndReset("Could not find the WebApp folder.");
                        return;
                    }

                    string distDir = Path.Combine(webAppDir, "dist");
                    string nodeModules = Path.Combine(webAppDir, "node_modules");

                    // Step 1: Install deps if needed
                    if (!Directory.Exists(nodeModules))
                    {
                        StatusText.Text = "Installing dependencies...";
                        SetProgress(0.15);
                        var npmInstall = await RunSilentAsync("cmd.exe", "/c npm install --omit=dev", webAppDir);
                        if (npmInstall != 0)
                        {
                            ShowErrorAndReset("Failed to install npm dependencies. Make sure Node.js is installed.");
                            return;
                        }
                    }

                    // Step 2: Build frontend only if dist is missing (dist is pre-built and embedded)
                    if (!Directory.Exists(distDir) || !File.Exists(Path.Combine(distDir, "index.html")))
                    {
                        StatusText.Text = "Building frontend...";
                        SetProgress(0.35);
                        var buildResult = await RunSilentAsync("cmd.exe", "/c npm run build", webAppDir);
                        if (buildResult != 0)
                        {
                            ShowErrorAndReset("Frontend build failed. Make sure Node.js is installed.");
                            return;
                        }
                    }
                    SetProgress(0.70);

                    // Step 3: Kill any stale server holding port 1337
                    StatusText.Text = "Starting server...";
                    SetProgress(0.80);
                    await RunSilentAsync("cmd.exe",
                        "/c for /f \"tokens=5\" %a in ('netstat -ano ^| findstr \":1337 \"') do taskkill /F /PID %a 2>nul",
                        AppPaths.DataRoot);
                    await Task.Delay(400);

                    // Step 4: Start the Express server (serves API + built frontend)
                    SetProgress(0.90);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c node server.js",
                        WorkingDirectory = webAppDir,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    // Step 5: Wait for server to be ready, then open browser
                    StatusText.Text = "Opening browser...";
                    SetProgress(1.0);
                    await Task.Delay(1800);
                    Process.Start(new ProcessStartInfo("http://localhost:1337") { UseShellExecute = true });

                    this.Close();
                }
                catch (Exception ex)
                {
                    ShowErrorAndReset($"Failed to launch web suite: {ex.Message}");
                }
            });
        }

        private string? FindWebAppDir()
        {
            // Prefer dev project root (has latest server.js changes)
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                string check = Path.Combine(current, "WebApp");
                if (Directory.Exists(check) && File.Exists(Path.Combine(check, "server.js")))
                    return check;
                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }

            // Fallback: LocalAppData extracted copy
            if (Directory.Exists(AppPaths.WebAppDir) && File.Exists(Path.Combine(AppPaths.WebAppDir, "server.js")))
                return AppPaths.WebAppDir;

            return null;
        }

        /// <summary>Animates the loading bar to a target fraction (0.0–1.0) of its parent width.</summary>
        private void SetProgress(double fraction)
        {
            double parentWidth = 460; // matches Width="460" on LoadingPanel
            double targetWidth = parentWidth * Math.Clamp(fraction, 0, 1);
            var anim = new DoubleAnimation(targetWidth, new Duration(TimeSpan.FromMilliseconds(350)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            LoadingBar.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, anim);
        }

        private void ShowErrorAndReset(string message)
        {
            MessageBox.Show(message);
            LoadingPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.3)));
            ChoiceGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.3)));
            ChoiceGrid.IsHitTestVisible = true;
        }

        private Task<int> RunSilentAsync(string fileName, string arguments, string workingDir)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<int>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                },
                EnableRaisingEvents = true
            };
            process.Exited += (s, e) =>
            {
                tcs.SetResult(process.ExitCode);
                process.Dispose();
            };
            process.Start();
            return tcs.Task;
        }

        private async Task TransitionAndAction(Action action)
        {
            ChoiceGrid.IsHitTestVisible = false;
            ChoiceGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.4)));
            await Task.Delay(600);
            action();
            if (action.Method.Name.Contains("Desktop")) this.Close();
        }

        private async Task TransitionAndAction(Func<Task> action)
        {
            ChoiceGrid.IsHitTestVisible = false;
            ChoiceGrid.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.4)));
            LoadingPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.5)));
            SetProgress(0.05);
            await Task.Delay(500);
            await action();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
        
        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        }
    }
}
