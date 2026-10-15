using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VideoWall3x2
{
    public class DashboardConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int Zoom { get; set; } = 100;
    }

    /// <summary>
    /// Área da parede (um ou mais monitores) que exibe um dashboard.
    /// </summary>
    public class WallTile
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int RowSpan { get; set; } = 1;
        public int ColumnSpan { get; set; } = 1;
        public string Dashboard { get; set; } = string.Empty;
    }

    /// <summary>
    /// Dashboard exibido em um monitor físico (modo janela por monitor).
    /// </summary>
    public class MonitorAssignment
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Dashboard { get; set; } = string.Empty;
    }

    public class VideoWallSettings
    {
        public const int MaxSize = 8;

        public int RefreshMinutes { get; set; } = 5;

        // Linhas x colunas de monitores físicos
        public int Rows { get; set; } = 2;
        public int Columns { get; set; } = 2;

        // true = janela cobre todos os monitores; false = só o principal
        public bool SpanAllMonitors { get; set; } = true;

        // true = abre uma janela em tela cheia em cada monitor
        public bool WindowPerMonitor { get; set; }

        public List<DashboardConfig> Dashboards { get; set; } = new();
        public List<WallTile> Tiles { get; set; } = new();
        public List<MonitorAssignment> Monitors { get; set; } = new();

        /// <summary>
        /// Garante grade válida: remove áreas fora da grade ou sobrepostas
        /// e preenche células livres com áreas 1x1.
        /// </summary>
        public void Normalize()
        {
            Rows = Math.Clamp(Rows, 1, MaxSize);
            Columns = Math.Clamp(Columns, 1, MaxSize);

            bool autoAssign = Tiles.Count == 0;
            var used = new bool[Rows, Columns];
            var valid = new List<WallTile>();

            foreach (var tile in Tiles)
            {
                if (tile.Row < 0 || tile.Column < 0 ||
                    tile.RowSpan < 1 || tile.ColumnSpan < 1 ||
                    tile.Row + tile.RowSpan > Rows ||
                    tile.Column + tile.ColumnSpan > Columns)
                    continue;

                if (Cells(tile).Any(c => used[c.r, c.c]))
                    continue;

                foreach (var c in Cells(tile))
                    used[c.r, c.c] = true;

                valid.Add(tile);
            }

            var enabled = Dashboards.Where(d => d.Enabled).ToList();
            int next = 0;

            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    if (used[r, c])
                        continue;

                    valid.Add(new WallTile
                    {
                        Row = r,
                        Column = c,
                        Dashboard = autoAssign && next < enabled.Count
                            ? enabled[next++].Name
                            : string.Empty
                    });
                }
            }

            Tiles = valid
                .OrderBy(t => t.Row)
                .ThenBy(t => t.Column)
                .ToList();
        }

        private static IEnumerable<(int r, int c)> Cells(WallTile t)
        {
            for (int r = t.Row; r < t.Row + t.RowSpan; r++)
                for (int c = t.Column; c < t.Column + t.ColumnSpan; c++)
                    yield return (r, c);
        }
    }

    public static class ConfigService
    {
        private static readonly string FilePath =
            Path.Combine(AppContext.BaseDirectory, "videowallsettings.json");

        private static readonly JsonSerializerOptions Options =
            new() { WriteIndented = true };

        public static VideoWallSettings Load()
        {
            if (!File.Exists(FilePath))
                throw new FileNotFoundException(
                    $"Arquivo de configuração não encontrado: {FilePath}");

            string json = File.ReadAllText(FilePath);

            var settings = JsonSerializer.Deserialize<VideoWallSettings>(json)
                           ?? new VideoWallSettings();

            settings.Normalize();
            return settings;
        }

        public static void Save(VideoWallSettings settings)
        {
            File.WriteAllText(
                FilePath,
                JsonSerializer.Serialize(settings, Options));
        }

        public static VideoWallSettings Clone(VideoWallSettings settings)
        {
            return JsonSerializer.Deserialize<VideoWallSettings>(
                       JsonSerializer.Serialize(settings))
                   ?? new VideoWallSettings();
        }
    }
}
