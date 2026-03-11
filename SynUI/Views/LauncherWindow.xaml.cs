using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
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
                        var npmInstall = await RunSilentAsync("cmd.exe", "/c npm install", webAppDir);
                        if (npmInstall != 0)
                        {
                            ShowErrorAndReset("Failed to install npm dependencies. Make sure Node.js is installed.");
                            return;
                        }
                    }

                    // Step 2: Build frontend if dist/ doesn't exist
                    if (!Directory.Exists(distDir))
                    {
                        StatusText.Text = "Building frontend...";
                        var buildResult = await RunSilentAsync("cmd.exe", "/c npm run build", webAppDir);
                        if (buildResult != 0)
                        {
                            ShowErrorAndReset("Frontend build failed.");
                            return;
                        }
                    }

                    // Step 3: Start the Express server (serves API + built frontend)
                    StatusText.Text = "Starting server...";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c node server.js",
                        WorkingDirectory = webAppDir,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });

                    // Step 4: Wait for server to be ready, then open browser
                    StatusText.Text = "Opening browser...";
                    await Task.Delay(1500);
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
            // First check LocalAppData (where we extract resources)
            if (Directory.Exists(AppPaths.WebAppDir)) return AppPaths.WebAppDir;

            // Fallback: check project root (dev environment)
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 7; i++)
            {
                string check = Path.Combine(current, "WebApp");
                if (Directory.Exists(check)) return check;

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current) break;
                current = parent;
            }
            return null;
        }

        private void ShowErrorAndReset(string message)
        {
            MessageBox.Show(message);
            StatusText.Opacity = 0;
            ChoiceGrid.Opacity = 1;
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
            
            // Nice fade out of choices
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.4));
            ChoiceGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            
            StatusText.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.5)));
            
            await Task.Delay(600);
            action();
            if (action.Method.Name.Contains("Desktop")) this.Close();
        }

        private async Task TransitionAndAction(Func<Task> action)
        {
            ChoiceGrid.IsHitTestVisible = false;
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.4));
            ChoiceGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            StatusText.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.5)));
            
            await Task.Delay(600);
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
