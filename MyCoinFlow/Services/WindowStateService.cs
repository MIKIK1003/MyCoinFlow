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

        // Umbenannt → kein Konflikt mehr mit System.Windows.WindowState
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
                // bewusst leer – UI darf nie crashen wegen State
            }
        }

        public static void Save(Window window)
        {
            try
            {
                if (window == null)
                    return;

                // Nur speichern wenn Fenster im normalen Zustand ist
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
                // kein Crash
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

                // Nur anwenden wenn noch sichtbar auf einem Screen
                if (IsVisibleOnAnyScreen(state))
                {
                    window.Top = state.Top;
                    window.Left = state.Left;
                    window.Width = state.Width;
                    window.Height = state.Height;
                }
            }
            catch
            {
                // kein Crash
            }
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