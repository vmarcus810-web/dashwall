using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace VideoWall3x2
{
    /// <summary>
    /// Monitor físico detectado no Windows. Coordenadas em pixels reais.
    /// </summary>
    public class MonitorInfo
    {
        public string DeviceName { get; init; } = string.Empty;
        public int Left { get; init; }
        public int Top { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public bool IsPrimary { get; init; }

        public override string ToString()
        {
            string primary = IsPrimary ? " (principal)" : string.Empty;
            return $"{DeviceName.Replace(@"\\.\", "")} - {Width}x{Height} em {Left},{Top}{primary}";
        }
    }

    public static class MonitorService
    {
        /// <summary>
        /// Monitores ordenados da esquerda para a direita, de cima para baixo.
        /// </summary>
        public static List<MonitorInfo> GetMonitors()
        {
            var monitors = new List<MonitorInfo>();

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };

                    if (GetMonitorInfo(hMonitor, ref info))
                    {
                        monitors.Add(new MonitorInfo
                        {
                            DeviceName = info.szDevice,
                            Left = info.rcMonitor.Left,
                            Top = info.rcMonitor.Top,
                            Width = info.rcMonitor.Right - info.rcMonitor.Left,
                            Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                            IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0
                        });
                    }

                    return true;
                },
                IntPtr.Zero);

            return monitors
                .OrderBy(m => m.Top)
                .ThenBy(m => m.Left)
                .ToList();
        }

        /// <summary>
        /// Move a janela para cobrir exatamente o retângulo (pixels reais).
        /// </summary>
        public static void MoveWindow(IntPtr hwnd, MonitorInfo monitor)
        {
            SetWindowPos(hwnd, IntPtr.Zero,
                monitor.Left, monitor.Top, monitor.Width, monitor.Height,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }

        private const uint MONITORINFOF_PRIMARY = 1;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private delegate bool MonitorEnumProc(
            IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(
            IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
