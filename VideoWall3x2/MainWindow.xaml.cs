using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VideoWall3x2;

namespace VideoWall
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer? _refreshTimer;

        private readonly List<WebView2> _browsers = new();

        private readonly VideoWallSettings _settings;

        // Monitor que a janela deve preencher (modo janela por monitor)
        private readonly MonitorInfo? _monitor;

        public MainWindow(VideoWallSettings settings, MonitorInfo? monitor = null)
        {
            InitializeComponent();

            _settings = settings;
            _monitor = monitor;

            if (_monitor == null)
                ApplyWindowBounds();
            else
                SourceInitialized += (_, _) =>
                    MonitorService.MoveWindow(
                        new WindowInteropHelper(this).Handle, _monitor);

            Loaded += MainWindow_Loaded;
            PreviewKeyDown += MainWindow_KeyDown;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                await BuildWall();
                StartRefresh();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        // Esc ou F2 fecha o wall e volta ao formulário de configuração
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Escape or Key.F2)
            {
                e.Handled = true;
                Close();
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _refreshTimer?.Stop();

            foreach (var browser in _browsers)
                browser.Dispose();

            _browsers.Clear();
        }

        private void ApplyWindowBounds()
        {
            WindowState = WindowState.Normal;

            if (_settings.SpanAllMonitors)
            {
                // Área virtual = todos os monitores juntos
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
            }
            else
            {
                Left = 0;
                Top = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                Height = SystemParameters.PrimaryScreenHeight;
            }
        }

        private async Task BuildWall()
        {
            foreach (var browser in _browsers)
                browser.Dispose();

            _browsers.Clear();
            WallGrid.Children.Clear();
            WallGrid.RowDefinitions.Clear();
            WallGrid.ColumnDefinitions.Clear();

            for (int r = 0; r < _settings.Rows; r++)
                WallGrid.RowDefinitions.Add(new RowDefinition());

            for (int c = 0; c < _settings.Columns; c++)
                WallGrid.ColumnDefinitions.Add(new ColumnDefinition());

            foreach (var tile in _settings.Tiles)
            {
                var border = new Border
                {
                    Margin = new Thickness(2),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
                    BorderThickness = new Thickness(1)
                };

                Grid.SetRow(border, tile.Row);
                Grid.SetColumn(border, tile.Column);
                Grid.SetRowSpan(border, tile.RowSpan);
                Grid.SetColumnSpan(border, tile.ColumnSpan);
                WallGrid.Children.Add(border);

                var dashboard = _settings.Dashboards.FirstOrDefault(
                    d => d.Enabled && d.Name == tile.Dashboard);

                if (dashboard == null)
                {
                    border.Child = new TextBlock
                    {
                        Text = "Sem dashboard (Esc para configurar)",
                        Foreground = Brushes.DimGray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    continue;
                }

                var browser = new WebView2();
                border.Child = browser;
                _browsers.Add(browser);

                await InitializeBrowser(browser, dashboard);
            }
        }

        private async Task InitializeBrowser(
            WebView2 browser,
            DashboardConfig config)
        {
            await browser.EnsureCoreWebView2Async();

            browser.CoreWebView2.Settings
                .AreDefaultContextMenusEnabled = false;

            browser.CoreWebView2.Settings
                .AreDevToolsEnabled = false;

            browser.CoreWebView2.Settings
                .AreBrowserAcceleratorKeysEnabled = false;

            browser.CoreWebView2.Settings
                .IsStatusBarEnabled = false;

            browser.CoreWebView2.Settings
                .IsZoomControlEnabled = false;

            browser.Source = new Uri(config.Url);

            browser.CoreWebView2.NavigationCompleted +=
                (_, __) =>
                {
                    browser.ZoomFactor =
                        config.Zoom / 100.0;
                };
        }

        private void StartRefresh()
        {
            _refreshTimer?.Stop();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(
                    Math.Max(1, _settings.RefreshMinutes))
            };

            _refreshTimer.Tick += (_, __) =>
            {
                foreach (var browser in _browsers)
                {
                    browser.Reload();
                }
            };

            _refreshTimer.Start();
        }

        private static void ShowError(Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
