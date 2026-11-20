using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace VibrationAnalysis.UI
{
    public class Theme
    {
        public string Name { get; set; }
        [JsonConverter(typeof(ColorJsonConverter))] public Color BackColor { get; set; }
        [JsonConverter(typeof(ColorJsonConverter))] public Color ForeColor { get; set; }
        [JsonConverter(typeof(ColorJsonConverter))] public Color ControlBackColor { get; set; }
        [JsonConverter(typeof(ColorJsonConverter))] public Color LineColor { get; set; }
        [JsonConverter(typeof(ColorJsonConverter))] public Color AccentColor { get; set; } = Color.FromArgb(14, 124, 134);
    }

    /// <summary>Guarda los colores como "#RRGGBB" (System.Text.Json no serializa System.Drawing.Color).</summary>
    public sealed class ColorJsonConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s)) return Color.Empty;
            try { return ColorTranslator.FromHtml(s); } catch { return Color.Empty; }
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"#{value.R:X2}{value.G:X2}{value.B:X2}");
    }

    /// <summary>Temas de color. Los temas personalizados se guardan en %LOCALAPPDATA%\AnalisisVibraciones\themes.json.</summary>
    public class ThemeManager
    {
        private readonly Dictionary<string, Theme> themes;
        private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnalisisVibraciones");
        private readonly string themeFilePath = Path.Combine(Dir, "themes.json");
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { WriteIndented = true };

        public ThemeManager()
        {
            themes = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase)
            {
                ["Claro"] = new Theme { Name = "Claro", BackColor = Color.FromArgb(246, 248, 248), ForeColor = Color.FromArgb(29, 35, 39), ControlBackColor = Color.White, LineColor = Color.FromArgb(14, 124, 134) },
                ["Oscuro"] = new Theme { Name = "Oscuro", BackColor = Color.FromArgb(28, 32, 35), ForeColor = Color.FromArgb(235, 240, 240), ControlBackColor = Color.FromArgb(44, 50, 54), LineColor = Color.FromArgb(77, 208, 225), AccentColor = Color.FromArgb(77, 208, 225) },
                ["Alto contraste"] = new Theme { Name = "Alto contraste", BackColor = Color.Black, ForeColor = Color.Yellow, ControlBackColor = Color.FromArgb(30, 30, 30), LineColor = Color.Yellow, AccentColor = Color.Yellow },
            };
            LoadThemes();
        }

        public List<string> GetAvailableThemes() => themes.Keys.ToList();

        public Theme GetTheme(string name) => name != null && themes.TryGetValue(name, out var t) ? t : null;

        public void SaveTheme(Theme theme)
        {
            themes[theme.Name] = theme;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(themeFilePath, JsonSerializer.Serialize(themes.Values.ToList(), JsonOpts));
        }

        public Theme LoadTheme(string filePath)
        {
            var theme = JsonSerializer.Deserialize<Theme>(File.ReadAllText(filePath), JsonOpts);
            if (theme == null || string.IsNullOrWhiteSpace(theme.Name)) throw new InvalidDataException("El archivo no contiene un tema válido.");
            themes[theme.Name] = theme;
            return theme;
        }

        public void ExportTheme(Theme theme, string filePath) => File.WriteAllText(filePath, JsonSerializer.Serialize(theme, JsonOpts));

        private void LoadThemes()
        {
            try
            {
                if (!File.Exists(themeFilePath)) return;
                var loaded = JsonSerializer.Deserialize<List<Theme>>(File.ReadAllText(themeFilePath), JsonOpts);
                if (loaded == null) return;
                foreach (var t in loaded.Where(t => !string.IsNullOrWhiteSpace(t.Name))) themes[t.Name] = t;
            }
            catch (Exception ex)
            {
                LogManager.Instance.LogWarning($"No se pudieron cargar los temas guardados: {ex.Message}");
            }
        }

        /// <summary>Aplica el tema en forma recursiva a un control y sus hijos (los gráficos se dibujan con sus propios colores).</summary>
        public static void Apply(Control root, Theme theme)
        {
            root.BackColor = theme.BackColor;
            root.ForeColor = theme.ForeColor;
            foreach (Control c in root.Controls) ApplyTo(c, theme);
        }

        private static void ApplyTo(Control c, Theme theme)
        {
            switch (c)
            {
                case SignalControl or SpectrumPlotControl or PolarGraphControl or BalancePolarControl:
                    return;
                case TextBox or NumericUpDown or ComboBox or ListView or ListBox or RichTextBox or PropertyGrid:
                    c.BackColor = theme.ControlBackColor;
                    c.ForeColor = theme.ForeColor;
                    if (c is PropertyGrid pg)
                    {
                        pg.ViewBackColor = theme.ControlBackColor; pg.ViewForeColor = theme.ForeColor;
                        pg.LineColor = theme.BackColor; pg.CategoryForeColor = theme.ForeColor; pg.HelpBackColor = theme.BackColor; pg.HelpForeColor = theme.ForeColor;
                    }
                    break;
                case Button b:
                    b.BackColor = theme.ControlBackColor;
                    b.ForeColor = theme.ForeColor;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = theme.AccentColor;
                    break;
                case TabPage or Panel or GroupBox or SplitContainer or TableLayoutPanel or FlowLayoutPanel or Label or CheckBox or RadioButton:
                    if (c.Tag as string != "keepColor")
                    {
                        c.BackColor = theme.BackColor;
                        c.ForeColor = theme.ForeColor;
                    }
                    break;
            }
            foreach (Control child in c.Controls) ApplyTo(child, theme);
        }
    }
}
