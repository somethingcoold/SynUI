using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace SynUI.Views
{
    public partial class LoadingWindow : Window
    {
        private const double ProgressBarMaxWidth = 220;

        public LoadingWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Start entrance animation
            var fadeIn = (Storyboard)FindResource("FadeIn");
            fadeIn.Begin();

            // Start glow pulse
            var pulseGlow = (Storyboard)FindResource("PulseGlow");
            pulseGlow.Begin();

            // Simulate loading with smooth progress
            await AnimateProgress();

            // Fade out and transition
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleOut = new DoubleAnimation(1, 1.06, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, _) =>
            {
                var mainWindow = new MainWindow();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                this.Close();
            };

            RootPanel.BeginAnimation(OpacityProperty, fadeOut);
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleOut);
            RootScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleOut);
        }

        private async Task AnimateProgress()
        {
            string[] stages = new[]
            {
                "Initializing...",
                "Loading workspace...",
                "Preparing editor...",
                "Almost ready..."
            };

            double progress = 0;
            int stageIndex = 0;
            var random = new Random();

            while (progress < 100)
            {
                // Update stage text at thresholds
                int newStage = progress switch
                {
                    < 20 => 0,
                    < 50 => 1,
                    < 80 => 2,
                    _ => 3
                };

                if (newStage != stageIndex)
                {
                    stageIndex = newStage;
                    LoadingStatusText.Text = stages[stageIndex];
                }

                // Variable speed: start fast, slow in middle, finish fast
                double increment = progress switch
                {
                    < 15 => random.NextDouble() * 4 + 2,     // Fast start
                    < 40 => random.NextDouble() * 2 + 1,     // Medium
                    < 70 => random.NextDouble() * 1.5 + 0.5, // Slower
                    < 90 => random.NextDouble() * 3 + 1.5,   // Speed up
                    _ => random.NextDouble() * 5 + 3          // Fast finish
                };

                progress = Math.Min(100, progress + increment);

                // Animate the bar width smoothly
                double targetWidth = (progress / 100.0) * ProgressBarMaxWidth;
                var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(80))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressFill.BeginAnimation(WidthProperty, widthAnim);

                // Update percentage text
                PercentageText.Text = $"{(int)progress}%";

                // Variable delay between updates
                int delay = progress switch
                {
                    < 15 => random.Next(30, 60),
                    < 40 => random.Next(40, 80),
                    < 70 => random.Next(50, 100),
                    < 90 => random.Next(30, 60),
                    _ => random.Next(20, 40)
                };

                await Task.Delay(delay);
            }

            // Ensure 100%
            PercentageText.Text = "100%";
            LoadingStatusText.Text = "Ready";

            var finalAnim = new DoubleAnimation(ProgressBarMaxWidth, TimeSpan.FromMilliseconds(100));
            ProgressFill.BeginAnimation(WidthProperty, finalAnim);

            await Task.Delay(400); // Brief pause at 100%
        }
    }
}
