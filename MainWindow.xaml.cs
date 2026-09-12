using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Diagnostics;
using System.IO;

namespace PalantirOptimization
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.4));
            fadeOut.Completed += async (s, ev) => 
            {
                
                await OptimizerCore.RestoreAsync();
                this.Close();
            };
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }

        private async void OptimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            OptimizeBtn.IsEnabled = false;
            OptimizeBtn.Content = "OPTIMIZING...";
            StatusText.Text = "Please wait...";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;
            
            var config = new OptimizationConfig
            {
                UnlockFPS = ChkUnlockFPS.IsChecked ?? false,
                ClearCache = ChkClearCache.IsChecked ?? false,
                HighPerformance = ChkHighPerformance.IsChecked ?? false,

                NetworkOpt = ChkNetworkOpt.IsChecked ?? false,
                TimerResolution = ChkTimerRes.IsChecked ?? false,
                DisableGameDVR = ChkGameDVR.IsChecked ?? false,
                ClearShaderCache = ChkShaderCache.IsChecked ?? false,
                AdvancedNetwork = ChkAdvancedNetwork.IsChecked ?? false,
                NetworkQoS = ChkNetworkQoS.IsChecked ?? false,
                AudioLatency = ChkAudioLatency.IsChecked ?? false,
                CloudflareDNS = ChkCloudflareDNS.IsChecked ?? false
            };
            
            var result = await OptimizerCore.OptimizeAsync(config);
            
            OptimizeBtn.Content = "OPTIMIZE";
            StatusText.Text = result.message;
            
            if (result.success)
            {
                StatusText.Foreground = System.Windows.Media.Brushes.MediumPurple;
                OptimizeBtn.IsEnabled = false; 
                RestoreBtn.IsEnabled = true;   
            }
            else
            {
                StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
                OptimizeBtn.Content = "OPTIMIZE";
                OptimizeBtn.IsEnabled = true;
            }
        }

        private async void RestoreBtn_Click(object sender, RoutedEventArgs e)
        {
            RestoreBtn.IsEnabled = false;
            RestoreBtn.Content = "RESTORING...";
            StatusText.Text = "Restoring apps and settings...";
            StatusText.Foreground = System.Windows.Media.Brushes.Gray;

            var result = await OptimizerCore.RestoreAsync();

            RestoreBtn.Content = "RESTORE";
            StatusText.Text = result.message;
            StatusText.Foreground = System.Windows.Media.Brushes.MediumPurple;
            
            OptimizeBtn.IsEnabled = true;
        }
    }
}
