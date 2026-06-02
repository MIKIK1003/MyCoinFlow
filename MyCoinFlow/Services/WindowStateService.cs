using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace MyCoinFlow.Services
{
    public static class WindowStateService
    {
        private static readonly string _folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCoinFlow");

        private static readonly string _file =
            Path.Combine(_folder, "windowstate.json");

        private class WindowPlacement
        {
            public double Top { get; set; }
            public double Left { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public WindowState WindowState { get; set; } = WindowState.Normal;
        }

        private static Dictionary<string, WindowPlacement> LoadAll()
        {
            try
            {
                if (!File.Exists(_file))
                    return new Dictionary<string, WindowPlacement>();

                var json = File.ReadAllText(_file);

                return JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(json)
                       ?? new Dictionary<string, WindowPlacement>();
            }
            catch
            {
                return new Dictionary<string, WindowPlacement>();
            }
        }

        private static void SaveAll(Dictionary<string, WindowPlacement> data)
        {
            try
            {
                if (!Directory.Exists(_folder))
                    Directory.CreateDirectory(_folder);

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_file, json);
            }
            catch
            {
                // UI darf nie crashen
            }
        }

        public static void Save(Window window)
        {
            try
            {
                if (window == null)
                    return;

                var all = LoadAll();
                var key = window.GetType().Name;

                var bounds = window.WindowState == WindowState.Normal
                    ? new Rect(window.Left, window.Top, window.Width, window.Height)
                    : window.RestoreBounds;

                if (!IsValidSize(bounds))
                    return;

                all[key] = new WindowPlacement
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Width = bounds.Width,
                    Height = bounds.Height,
                    WindowState = window.WindowState == WindowState.Maximized
                        ? WindowState.Maximized
                        : WindowState.Normal
                };

                SaveAll(all);
            }
            catch
            {
                // UI darf nie crashen
            }
        }

        public static void Restore(Window window)
        {
            try
            {
                if (window == null)
                    return;

                var all = LoadAll();
                var key = window.GetType().Name;

                if (!all.TryGetValue(key, out var state))
                    return;

                if (!IsValidSize(state))
                    return;

                var rect = new Rect(state.Left, state.Top, state.Width, state.Height);
                var safeRect = EnsureWindowVisible(rect);

                window.WindowState = WindowState.Normal;

                window.Left = safeRect.Left;
                window.Top = safeRect.Top;
                window.Width = safeRect.Width;
                window.Height = safeRect.Height;

                if (state.WindowState == WindowState.Maximized)
                    window.WindowState = WindowState.Maximized;
            }
            catch
            {
                // UI darf nie crashen
            }
        }

        private static Rect EnsureWindowVisible(Rect rect)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var wa = screen.WorkingArea;

                var screenRect = new Rect(
                    wa.Left,
                    wa.Top,
                    wa.Width,
                    wa.Height);

                if (screenRect.IntersectsWith(rect))
                    return rect;
            }

            var primary = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;

            double width = Math.Min(rect.Width, primary.Width);
            double height = Math.Min(rect.Height, primary.Height);

            double left = primary.Left + (primary.Width - width) / 2;
            double top = primary.Top + (primary.Height - height) / 2;

            return new Rect(left, top, width, height);
        }

        private static bool IsValidSize(WindowPlacement s)
        {
            return IsValidSize(new Rect(s.Left, s.Top, s.Width, s.Height));
        }

        private static bool IsValidSize(Rect rect)
        {
            if (rect.Width < 300 || rect.Height < 200)
                return false;

            if (rect.Width > 4000 || rect.Height > 3000)
                return false;

            if (double.IsNaN(rect.Width) || double.IsNaN(rect.Height))
                return false;

            if (double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height))
                return false;

            return true;
        }
    }
}