using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VideoWall3x2;

namespace VideoWall
{
    public partial class ConfigWindow : Window
    {
        private readonly VideoWallSettings _settings;

        private readonly ObservableCollection<DashboardConfig> _dashboards;

        private readonly HashSet<WallTile> _selected = new();

        private readonly bool _loading;

        private string? _editingName;

        private List<MonitorInfo> _monitors = new();

        public VideoWallSettings Result => _settings;

        public ConfigWindow(VideoWallSettings current)
        {
            _loading = true;

            InitializeComponent();

            _settings = ConfigService.Clone(current);
            _settings.Normalize();

            _dashboards = new ObservableCollection<DashboardConfig>(_settings.Dashboards);
            DashGrid.ItemsSource = _dashboards;

            for (int i = 1; i <= VideoWallSettings.MaxSize; i++)
            {
                RowsBox.Items.Add(i);
                ColsBox.Items.Add(i);
            }

            RowsBox.SelectedItem = _settings.Rows;
            ColsBox.SelectedItem = _settings.Columns;
            RefreshBox.Text = _settings.RefreshMinutes.ToString();
            SpanBox.IsChecked = _settings.SpanAllMonitors;

            GridModeRadio.IsChecked = !_settings.WindowPerMonitor;
            MonitorModeRadio.IsChecked = _settings.WindowPerMonitor;

            _loading = false;

            RenderPreview();
            DetectMonitors();
        }

        // ---------- Grade ----------

        private void GridSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || RowsBox.SelectedItem is not int rows ||
                ColsBox.SelectedItem is not int cols)
                return;

            var old = _settings.Tiles;

            _settings.Rows = rows;
            _settings.Columns = cols;
            _settings.Tiles = new List<WallTile>();

            // Mantém o dashboard de cada célula que já existia
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    _settings.Tiles.Add(new WallTile
                    {
                        Row = r,
                        Column = c,
                        Dashboard = old.FirstOrDefault(t => Contains(t, r, c))?.Dashboard
                                    ?? string.Empty
                    });

            _selected.Clear();
            RenderPreview();
        }

        private void MergeAll_Click(object sender, RoutedEventArgs e)
        {
            string dashboard = _settings.Tiles
                .Select(t => t.Dashboard)
                .FirstOrDefault(d => d.Length > 0)
                ?? _dashboards.FirstOrDefault(d => d.Enabled)?.Name
                ?? string.Empty;

            _settings.Tiles = new List<WallTile>
            {
                new()
                {
                    RowSpan = _settings.Rows,
                    ColumnSpan = _settings.Columns,
                    Dashboard = dashboard
                }
            };

            _selected.Clear();
            RenderPreview();
        }

        private void SplitAll_Click(object sender, RoutedEventArgs e)
        {
            var enabled = _dashboards.Where(d => d.Enabled).ToList();

            _settings.Tiles = new List<WallTile>();

            for (int r = 0; r < _settings.Rows; r++)
            {
                for (int c = 0; c < _settings.Columns; c++)
                {
                    int i = r * _settings.Columns + c;

                    _settings.Tiles.Add(new WallTile
                    {
                        Row = r,
                        Column = c,
                        Dashboard = i < enabled.Count ? enabled[i].Name : string.Empty
                    });
                }
            }

            _selected.Clear();
            RenderPreview();
        }

        private void MergeSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected.Count < 2)
            {
                Warn("Selecione duas ou mais células para mesclar.");
                return;
            }

            int top = _selected.Min(t => t.Row);
            int left = _selected.Min(t => t.Column);
            int bottom = _selected.Max(t => t.Row + t.RowSpan);
            int right = _selected.Max(t => t.Column + t.ColumnSpan);

            // As células não se sobrepõem: a seleção forma um retângulo
            // completo só se a soma das áreas for igual à do retângulo.
            int area = _selected.Sum(t => t.RowSpan * t.ColumnSpan);

            if (area != (bottom - top) * (right - left))
            {
                Warn("A seleção precisa formar um retângulo.");
                return;
            }

            var merged = new WallTile
            {
                Row = top,
                Column = left,
                RowSpan = bottom - top,
                ColumnSpan = right - left,
                Dashboard = _selected
                    .OrderBy(t => t.Row).ThenBy(t => t.Column)
                    .Select(t => t.Dashboard)
                    .FirstOrDefault(d => d.Length > 0) ?? string.Empty
            };

            _settings.Tiles.RemoveAll(_selected.Contains);
            _settings.Tiles.Add(merged);

            _selected.Clear();
            RenderPreview();
        }

        private void SplitSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected.Count == 0)
            {
                Warn("Selecione as células que deseja separar.");
                return;
            }

            foreach (var tile in _selected)
            {
                _settings.Tiles.Remove(tile);

                for (int r = tile.Row; r < tile.Row + tile.RowSpan; r++)
                    for (int c = tile.Column; c < tile.Column + tile.ColumnSpan; c++)
                        _settings.Tiles.Add(new WallTile
                        {
                            Row = r,
                            Column = c,
                            Dashboard = r == tile.Row && c == tile.Column
                                ? tile.Dashboard
                                : string.Empty
                        });
            }

            _selected.Clear();
            RenderPreview();
        }

        private void RenderPreview()
        {
            PreviewGrid.Children.Clear();
            PreviewGrid.RowDefinitions.Clear();
            PreviewGrid.ColumnDefinitions.Clear();

            for (int r = 0; r < _settings.Rows; r++)
                PreviewGrid.RowDefinitions.Add(new RowDefinition());

            for (int c = 0; c < _settings.Columns; c++)
                PreviewGrid.ColumnDefinitions.Add(new ColumnDefinition());

            foreach (var tile in _settings.Tiles)
            {
                var combo = new ComboBox
                {
                    ItemsSource = DashboardNames(),
                    SelectedItem = tile.Dashboard,
                    Margin = new Thickness(6),
                    MinWidth = 80
                };

                combo.DropDownOpened += (_, _) =>
                {
                    string current = tile.Dashboard;
                    combo.ItemsSource = DashboardNames();
                    combo.SelectedItem = current;
                };

                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string name)
                        tile.Dashboard = name;
                };

                string span = tile.RowSpan * tile.ColumnSpan > 1
                    ? $"  ({tile.RowSpan}x{tile.ColumnSpan} monitores)"
                    : string.Empty;

                var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                panel.Children.Add(new TextBlock
                {
                    Text = $"L{tile.Row + 1} C{tile.Column + 1}{span}",
                    Foreground = Brushes.LightGray,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                panel.Children.Add(combo);

                bool selected = _selected.Contains(tile);

                var border = new Border
                {
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(selected ? 3 : 1),
                    BorderBrush = new SolidColorBrush(
                        selected ? Color.FromRgb(0x22, 0xD3, 0xEE) : Color.FromRgb(0x33, 0x41, 0x55)),
                    Background = new SolidColorBrush(
                        selected ? Color.FromRgb(0x0E, 0x3A, 0x47) : Color.FromRgb(0x1E, 0x29, 0x3B)),
                    Child = panel,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                border.MouseLeftButtonDown += (_, _) =>
                {
                    if (!_selected.Remove(tile))
                        _selected.Add(tile);

                    RenderPreview();
                };

                Grid.SetRow(border, tile.Row);
                Grid.SetColumn(border, tile.Column);
                Grid.SetRowSpan(border, tile.RowSpan);
                Grid.SetColumnSpan(border, tile.ColumnSpan);
                PreviewGrid.Children.Add(border);
            }
        }

        private List<string> DashboardNames()
        {
            var names = new List<string> { string.Empty };
            names.AddRange(_dashboards.Select(d => d.Name).Where(n => n.Length > 0));
            return names;
        }

        private static bool Contains(WallTile t, int r, int c)
        {
            return r >= t.Row && r < t.Row + t.RowSpan &&
                   c >= t.Column && c < t.Column + t.ColumnSpan;
        }

        // ---------- Janela por monitor ----------

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            bool perMonitor = MonitorModeRadio.IsChecked == true;

            MonitorPanel.Visibility = perMonitor ? Visibility.Visible : Visibility.Collapsed;
            GridPanel.Visibility = perMonitor ? Visibility.Collapsed : Visibility.Visible;
        }

        private void DetectMonitors_Click(object sender, RoutedEventArgs e)
        {
            DetectMonitors();
        }

        private void DetectMonitors()
        {
            _monitors = MonitorService.GetMonitors();

            // Primeira vez: distribui os dashboards ativos pelos monitores
            if (_settings.Monitors.Count == 0)
            {
                var enabled = _dashboards.Where(d => d.Enabled).ToList();

                for (int i = 0; i < _monitors.Count; i++)
                    _settings.Monitors.Add(new MonitorAssignment
                    {
                        DeviceName = _monitors[i].DeviceName,
                        Dashboard = i < enabled.Count ? enabled[i].Name : string.Empty
                    });
            }

            RenderMonitors();
        }

        private void RenderMonitors()
        {
            MonitorList.Children.Clear();

            for (int i = 0; i < _monitors.Count; i++)
            {
                var monitor = _monitors[i];
                var assignment = GetAssignment(monitor);

                var combo = new ComboBox
                {
                    ItemsSource = DashboardNames(),
                    SelectedItem = assignment.Dashboard,
                    Width = 200
                };

                combo.DropDownOpened += (_, _) =>
                {
                    string current = assignment.Dashboard;
                    combo.ItemsSource = DashboardNames();
                    combo.SelectedItem = current;
                };

                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string name)
                        assignment.Dashboard = name;
                };

                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                DockPanel.SetDock(combo, Dock.Right);
                row.Children.Add(combo);
                row.Children.Add(new TextBlock
                {
                    Text = $"Monitor {i + 1}: {monitor}",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                MonitorList.Children.Add(row);
            }

            if (_monitors.Count == 0)
                MonitorList.Children.Add(new TextBlock { Text = "Nenhum monitor detectado." });
        }

        private MonitorAssignment GetAssignment(MonitorInfo monitor)
        {
            var assignment = _settings.Monitors.FirstOrDefault(
                m => m.DeviceName == monitor.DeviceName);

            if (assignment == null)
            {
                assignment = new MonitorAssignment { DeviceName = monitor.DeviceName };
                _settings.Monitors.Add(assignment);
            }

            return assignment;
        }

        // ---------- Dashboards ----------

        private void AddDashboard_Click(object sender, RoutedEventArgs e)
        {
            var dashboard = new DashboardConfig { Name = $"Dashboard {_dashboards.Count + 1}" };
            _dashboards.Add(dashboard);
            DashGrid.SelectedItem = dashboard;
            DashGrid.ScrollIntoView(dashboard);
        }

        private void RemoveDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (DashGrid.SelectedItem is not DashboardConfig dashboard)
                return;

            _dashboards.Remove(dashboard);

            foreach (var tile in _settings.Tiles.Where(t => t.Dashboard == dashboard.Name))
                tile.Dashboard = string.Empty;

            foreach (var m in _settings.Monitors.Where(m => m.Dashboard == dashboard.Name))
                m.Dashboard = string.Empty;

            RenderPreview();
            RenderMonitors();
        }

        private void DashGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            _editingName = (e.Row.Item as DashboardConfig)?.Name;
        }

        private void DashGrid_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit ||
                e.Row.Item is not DashboardConfig dashboard)
                return;

            string? oldName = _editingName;

            // Valor só é gravado após o evento; propaga renomeação depois
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (oldName != null && oldName != dashboard.Name)
                {
                    foreach (var tile in _settings.Tiles.Where(t => t.Dashboard == oldName))
                        tile.Dashboard = dashboard.Name;

                    foreach (var m in _settings.Monitors.Where(m => m.Dashboard == oldName))
                        m.Dashboard = dashboard.Name;
                }

                RenderPreview();
                RenderMonitors();
            });
        }

        // ---------- Salvar ----------

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (ApplyAndSave())
                Info("Configuração salva.");
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (ApplyAndSave())
                StartWall();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Abre o video wall com a configuração atual e esconde o formulário.
        /// Ao fechar o wall o formulário volta.
        /// </summary>
        public void StartWall()
        {
            var walls = new List<MainWindow>();

            if (_settings.WindowPerMonitor)
            {
                foreach (var monitor in MonitorService.GetMonitors())
                {
                    // Cada janela é uma grade 1x1 com o dashboard do monitor
                    var settings = ConfigService.Clone(_settings);
                    settings.Rows = 1;
                    settings.Columns = 1;
                    settings.Tiles = new List<WallTile>
                    {
                        new() { Dashboard = GetAssignment(monitor).Dashboard }
                    };

                    walls.Add(new MainWindow(settings, monitor));
                }
            }
            else
            {
                walls.Add(new MainWindow(ConfigService.Clone(_settings)));
            }

            bool closing = false;

            // Fechar qualquer janela fecha todas e volta ao formulário
            foreach (var wall in walls)
            {
                wall.Closed += (_, _) =>
                {
                    if (closing)
                        return;

                    closing = true;

                    foreach (var other in walls.Where(w => w != wall))
                        other.Close();

                    Show();
                    Activate();
                };
            }

            Hide();

            foreach (var wall in walls)
                wall.Show();
        }

        private bool ApplyAndSave()
        {
            if (!TryApply())
                return false;

            try
            {
                ConfigService.Save(_settings);
                return true;
            }
            catch (Exception ex)
            {
                Warn($"Não foi possível salvar: {ex.Message}");
                return false;
            }
        }

        private bool TryApply()
        {
            DashGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (!int.TryParse(RefreshBox.Text, out int refresh) || refresh < 1)
            {
                Warn("Intervalo de atualização inválido.");
                return false;
            }

            if (_dashboards.Any(d => string.IsNullOrWhiteSpace(d.Name)) ||
                _dashboards.GroupBy(d => d.Name).Any(g => g.Count() > 1))
            {
                Warn("Cada dashboard precisa de um nome único.");
                return false;
            }

            var invalid = _dashboards.FirstOrDefault(
                d => !Uri.TryCreate(d.Url, UriKind.Absolute, out _));

            if (invalid != null)
            {
                Warn($"URL inválida no dashboard \"{invalid.Name}\".");
                return false;
            }

            _settings.RefreshMinutes = refresh;
            _settings.SpanAllMonitors = SpanBox.IsChecked == true;
            _settings.WindowPerMonitor = MonitorModeRadio.IsChecked == true;
            _settings.Dashboards = _dashboards.ToList();
            _settings.Normalize();

            return true;
        }

        private void Info(string message)
        {
            MessageBox.Show(this, message, "Video Wall",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "Video Wall",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
