using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
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

                if (window.WindowState != System.Windows.WindowState.Normal)
                    return;

                var all = LoadAll();
                var key = window.GetType().Name;

                all[key] = new WindowPlacement
                {
                    Top = window.Top,
                    Left = window.Left,
                    Width = window.Width,
                    Height = window.Height
                };

                SaveAll(all);
            }
            catch
            {
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

                if (!all.ContainsKey(key))
                    return;

                var state = all[key];

                // Plausibilitätsprüfung
                if (!IsValidSize(state))
                    return;

                var rect = new Rect(state.Left, state.Top, state.Width, state.Height);

                // 🔴 NEU: immer auf gültige Screen-Position bringen
                var safeRect = EnsureWindowVisible(rect);

                window.Left = safeRect.Left;
                window.Top = safeRect.Top;
                window.Width = safeRect.Width;
                window.Height = safeRect.Height;
            }
            catch
            {
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

                // Wenn Fenster irgendwo sichtbar ist → ok
                if (screenRect.IntersectsWith(rect))
                {
                    return rect;
                }
            }

            // 🔴 Fallback: Hauptmonitor zentriert
            var primary = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;

            double width = Math.Min(rect.Width, primary.Width);
            double height = Math.Min(rect.Height, primary.Height);

            double left = primary.Left + (primary.Width - width) / 2;
            double top = primary.Top + (primary.Height - height) / 2;

            return new Rect(left, top, width, height);
        }

        // 🔴 NEU
        private static bool IsValidSize(WindowPlacement s)
        {
            // Minimum sinnvoll
            if (s.Width < 300 || s.Height < 200)
                return false;

            // Maximum sinnvoll (Schutz vor „explodierten“ Werten)
            if (s.Width > 2000 || s.Height > 1500)
                return false;

            // NaN / Infinity Schutz
            if (double.IsNaN(s.Width) || double.IsNaN(s.Height))
                return false;

            if (double.IsInfinity(s.Width) || double.IsInfinity(s.Height))
                return false;

            return true;
        }

        private static bool IsVisibleOnAnyScreen(WindowPlacement state)
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var bounds = screen.WorkingArea;

                if (state.Left < bounds.Right &&
                    state.Left + state.Width > bounds.Left &&
                    state.Top < bounds.Bottom &&
                    state.Top + state.Height > bounds.Top)
                {
                    return true;
                }
            }

            return false;
        }
    }
}