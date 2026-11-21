using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VibrationAnalysis.Core.Report;

/// <summary>
/// Conversor Markdown → HTML mínimo (el subconjunto que usa el manual): títulos con anclas al estilo GitHub, párrafos,
/// listas anidadas, tablas, citas, bloques de código, separadores, negrita, cursiva, código y enlaces. Sin dependencias.
/// </summary>
public static class MarkdownHtml
{
    /// <summary>Convierte el documento a una página HTML autocontenida. Los enlaces relativos se resuelven contra <paramref name="baseUrl"/>.</summary>
    public static string ToPage(string markdown, string title, string baseUrl = "")
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>")
          .Append(E(title)).Append("</title><style>");
        sb.Append("body{font:15px/1.6 system-ui,'Segoe UI',Arial,sans-serif;color:#1d2327;max-width:900px;margin:24px auto;padding:0 20px}");
        sb.Append("h1{font-size:28px;border-bottom:3px solid #0e7c86;padding-bottom:6px}h2{font-size:21px;color:#0e7c86;border-bottom:1px solid #cfd8dc;padding-bottom:4px;margin-top:34px}h3{font-size:17px}");
        sb.Append("table{border-collapse:collapse;width:100%;margin:10px 0;font-size:14px}td,th{border:1px solid #cfd8dc;padding:5px 8px;text-align:left;vertical-align:top}th{background:#eef4f5}");
        sb.Append("code{background:#eef4f5;padding:1px 5px;border-radius:4px;font-size:13px}pre{background:#f4f7f8;border:1px solid #e3e9eb;padding:10px 12px;overflow-x:auto;border-radius:6px}pre code{background:none;padding:0}");
        sb.Append("blockquote{margin:12px 0;padding:6px 14px;border-left:4px solid #d28a00;background:#fff8e6}a{color:#0e7c86}hr{border:0;border-top:1px solid #cfd8dc;margin:24px 0}");
        sb.Append("@media print{body{max-width:none;margin:0}h2{break-after:avoid}table,pre{break-inside:avoid}}</style></head><body>");
        sb.Append(ToHtml(markdown, baseUrl));
        sb.Append("</body></html>");
        return sb.ToString();
    }

    public static string ToHtml(string markdown, string baseUrl = "")
    {
        var lines = markdown.Replace("\r", "").Split('\n');
        var sb = new StringBuilder();
        var para = new List<string>();
        var lists = new Stack<ListLevel>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            sb.Append("<p>").Append(Inline(string.Join(" ", para), baseUrl)).Append("</p>\n");
            para.Clear();
        }
        void CloseLists(int toIndent = -1)
        {
            while (lists.Count > 0 && lists.Peek().Indent > toIndent)
            {
                var l = lists.Pop();
                if (l.ItemOpen) sb.Append("</li>");
                sb.Append("</").Append(l.Tag).Append(">\n");
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string t = line.Trim();

            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                FlushPara(); CloseLists();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                sb.Append("<pre><code>").Append(E(string.Join("\n", code))).Append("</code></pre>\n");
                continue;
            }
            if (t.Length == 0) { FlushPara(); if (!NextIsListItem(lines, i)) CloseLists(); continue; }

            var h = Regex.Match(t, @"^(#{1,6})\s+(.*)$");
            if (h.Success)
            {
                FlushPara(); CloseLists();
                int lv = h.Groups[1].Length;
                string text = h.Groups[2].Value;
                sb.Append(CultureInfo.InvariantCulture, $"<h{lv} id=\"{E(Slug(text))}\">").Append(Inline(text, baseUrl)).Append(CultureInfo.InvariantCulture, $"</h{lv}>\n");
                continue;
            }
            if (t == "---" || t == "***") { FlushPara(); CloseLists(); sb.Append("<hr>\n"); continue; }

            if (t.StartsWith('>'))
            {
                FlushPara(); CloseLists();
                var q = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('>'); i++) q.Add(lines[i].Trim().TrimStart('>').Trim());
                i--;
                sb.Append("<blockquote><p>").Append(Inline(string.Join(" ", q), baseUrl)).Append("</p></blockquote>\n");
                continue;
            }

            if (t.StartsWith('|') && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|?\s*:?-{2,}"))
            {
                FlushPara(); CloseLists();
                var head = Cells(t);
                var align = Cells(lines[i + 1].Trim()).Select(c => c.StartsWith(':') && c.EndsWith(':') ? "center" : c.EndsWith(':') ? "right" : "").ToList();
                string A(int k) => k < align.Count && align[k].Length > 0 ? $" style=\"text-align:{align[k]}\"" : "";
                sb.Append("<table><thead><tr>");
                for (int k = 0; k < head.Count; k++) sb.Append(CultureInfo.InvariantCulture, $"<th{A(k)}>").Append(Inline(head[k], baseUrl)).Append("</th>");
                sb.Append("</tr></thead><tbody>");
                for (i += 2; i < lines.Length && lines[i].Trim().StartsWith('|'); i++)
                {
                    var row = Cells(lines[i].Trim());
                    sb.Append("<tr>");
                    for (int k = 0; k < row.Count; k++) sb.Append(CultureInfo.InvariantCulture, $"<td{A(k)}>").Append(Inline(row[k], baseUrl)).Append("</td>");
                    sb.Append("</tr>");
                }
                i--;
                sb.Append("</tbody></table>\n");
                continue;
            }

            var li = Regex.Match(line, @"^(\s*)([-*]|\d+\.)\s+(.*)$");
            if (li.Success)
            {
                FlushPara();
                int indent = li.Groups[1].Value.Length;
                string tag = char.IsDigit(li.Groups[2].Value[0]) ? "ol" : "ul";
                if (lists.Count > 0 && indent <= lists.Peek().Indent) CloseLists(indent);
                if (lists.Count > 0 && lists.Peek().Indent == indent && lists.Peek().Tag != tag) CloseLists(indent - 1);
                if (lists.Count == 0 || indent > lists.Peek().Indent)
                {
                    sb.Append('<').Append(tag).Append('>');
                    lists.Push(new ListLevel(tag, indent));
                }
                var cur = lists.Peek();
                if (cur.ItemOpen) sb.Append("</li>");
                sb.Append("<li>").Append(Inline(li.Groups[3].Value, baseUrl));
                cur.ItemOpen = true;
                continue;
            }

            if (lists.Count > 0 && line.StartsWith(' ')) { sb.Append(' ').Append(Inline(t, baseUrl)); continue; }
            CloseLists();
            para.Add(t);
        }
        FlushPara(); CloseLists();
        return sb.ToString();
    }

    private sealed class ListLevel
    {
        public ListLevel(string tag, int indent) { Tag = tag; Indent = indent; }
        public string Tag { get; }
        public int Indent { get; }
        public bool ItemOpen { get; set; }
    }

    /// <summary>Ancla de un título igual que GitHub: minúsculas, sin puntuación, espacios a guiones.</summary>
    public static string Slug(string heading)
    {
        string s = Regex.Replace(heading, @"[*`]|\[([^\]]*)\]\([^)]*\)", "$1").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (char c in s)
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        return sb.ToString();
    }

    private static bool NextIsListItem(string[] lines, int i)
    {
        for (int k = i + 1; k < lines.Length; k++)
        {
            if (lines[k].Trim().Length == 0) continue;
            return Regex.IsMatch(lines[k], @"^\s+([-*]|\d+\.)\s+");
        }
        return false;
    }

    private static List<string> Cells(string row)
    {
        row = row.Trim();
        if (row.StartsWith('|')) row = row[1..];
        if (row.EndsWith('|')) row = row[..^1];
        return row.Split('|').Select(c => c.Trim()).ToList();
    }

    private static string Inline(string s, string baseUrl)
    {
        var codes = new List<string>();
        s = Regex.Replace(s, @"`([^`]+)`", m => { codes.Add(m.Groups[1].Value); return $"\u0001{codes.Count - 1}\u0001"; });
        s = E(s);
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)\)", m =>
        {
            string url = m.Groups[2].Value.Replace("&amp;", "&");
            bool external = Regex.IsMatch(url, @"^[a-z]+:", RegexOptions.IgnoreCase);
            if (!external && !url.StartsWith('#') && baseUrl.Length > 0) url = Combine(baseUrl, url);
            string target = url.StartsWith('#') ? "" : " target=\"_blank\" rel=\"noopener\"";
            return $"<a href=\"{E(url)}\"{target}>{m.Groups[1].Value}</a>";
        });
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
        s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "<i>$1</i>");
        s = Regex.Replace(s, "\u0001(\\d+)\u0001", m => "<code>" + E(codes[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]) + "</code>");
        return s;
    }

    private static string Combine(string baseUrl, string rel)
    {
        var parts = baseUrl.TrimEnd('/').Split('/').ToList();
        foreach (var p in rel.Split('/'))
        {
            if (p == "..") { if (parts.Count > 3) parts.RemoveAt(parts.Count - 1); }
            else if (p != ".") parts.Add(p);
        }
        return string.Join("/", parts);
    }

    // Solo los caracteres especiales de HTML: los acentos quedan legibles (la página es UTF-8)
    private static string E(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
}
