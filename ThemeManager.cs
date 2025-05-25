using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VibrationAnalysis.UI
{
    public class Theme
    {
        public string Name { get; set; }
        public Color BackColor { get; set; }
        public Color ForeColor { get; set; }
        public Color ControlBackColor { get; set; }
        public Color LineColor { get; set; }
    }

    public class ThemeManager
    {
        private readonly Dictionary<string, Theme> themes;
        private readonly string themeFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "themes.json");

        public ThemeManager()
        {
            themes = new Dictionary<string, Theme>
            {
                {
                    "Light", new Theme
                    {
                        Name = "Light",
                        BackColor = SystemColors.Control,
                        ForeColor = SystemColors.ControlText,
                        ControlBackColor = SystemColors.Window,
                        LineColor = Color.Blue
                    }
                },
                {
                    "Dark", new Theme
                    {
                        Name = "Dark",
                        BackColor = Color.FromArgb(30, 30, 30),
                        ForeColor = Color.White,
                        ControlBackColor = Color.FromArgb(50, 50, 50),
                        LineColor = Color.Blue
                    }
                },
                {
                    "Custom1", new Theme
                    {
                        Name = "Custom1",
                        BackColor = Color.LightGray,
                        ForeColor = Color.DarkBlue,
                        ControlBackColor = Color.White,
                        LineColor = Color.Green
                    }
                },
                {
                    "Custom2", new Theme
                    {
                        Name = "Custom2",
                        BackColor = Color.Black,
                        ForeColor = Color.Yellow,
                        ControlBackColor = Color.DarkGray,
                        LineColor = Color.Red
                    }
                }
            };
            LoadThemes();
        }

        public List<string> GetAvailableThemes()
        {
            return themes.Keys.ToList();
        }

        public Theme GetTheme(string name)
        {
            return themes.ContainsKey(name) ? themes[name] : null;
        }

        public void SaveTheme(Theme theme)
        {
            themes[theme.Name] = theme;
            var allThemes = themes.Values.ToList();
            File.WriteAllText(themeFilePath, JsonSerializer.Serialize(allThemes, new JsonSerializerOptions { WriteIndented = true }));
        }

        public Theme LoadTheme(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var theme = JsonSerializer.Deserialize<Theme>(json);
            themes[theme.Name] = theme;
            return theme;
        }

        private void LoadThemes()
        {
            if (File.Exists(themeFilePath))
            {
                var json = File.ReadAllText(themeFilePath);
                var loadedThemes = JsonSerializer.Deserialize<List<Theme>>(json);
                foreach (var theme in loadedThemes)
                {
                    themes[theme.Name] = theme;
                }
            }
        }
    }
}