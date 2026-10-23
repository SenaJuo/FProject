using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 영어로 보기(2026-10-21: 설정 → 화면 → 언어). 코드에는 한국어 글자가 그대로 있고,
/// 글자를 그리는 곳(UIText)에서 T()로 영어로 바꾼다. 번역표는 LocEn.cs(자동 생성).
///  1) 글자 그대로 같은 것 → 2) 여러 줄이면 줄마다 → 3) 색 꼬리표가 있으면 꼬리표 사이마다 →
///  4) {…} 자리에 값이 들어간 것(정규식) → 5) " · "로 이어 붙인 것은 조각마다. 못 찾으면 한국어 그대로(Misses에 남긴다)
/// </summary>
public static partial class Loc
{
    public static bool English => GameSettings.English;

    static Dictionary<string, string> exact;
    static List<(Regex re, string[] names, string en)> templates;
    static readonly Dictionary<string, string> cache = new();
    /// <summary>번역을 못 찾은 한국어(시험·점검용)</summary>
    public static readonly HashSet<string> Misses = new();

    static readonly Regex Hole = new(@"\{[^{}]*\}");
    static readonly Regex Tag = new(@"(<[^<>]+>)");
    static readonly Regex NumberHole = new(@"Count|charges|current|spare|spent|remaining|\{dm\}|gained|Price|value|Seconds|\{n\}|count|\{sec\}|tenths|Min\(|metres");

    /// <summary>영어로 보기면 영어로, 아니면 그대로</summary>
    public static string T(string text) => English ? En(text) : text;

    /// <summary>설정과 상관없이 영어로(영어 튜토리얼 글을 만들 때)</summary>
    public static string En(string text)
    {
        if (string.IsNullOrEmpty(text) || !HasHangul(text)) return text;
        if (cache.TryGetValue(text, out var hit)) return hit;
        Build();
        var result = Translate(text, 0) ?? Miss(text);
        if (cache.Count > 4000) cache.Clear(); // 숫자가 매번 바뀌는 글자가 쌓이지 않게
        cache[text] = result;
        return result;
    }

    static string Miss(string text) { if (Misses.Count > 500) Misses.Clear(); Misses.Add(text); return text; }

    public static bool HasHangul(string s)
    {
        foreach (char c in s) if (c >= '가' && c <= '힣' || c >= 'ㄱ' && c <= 'ㆎ') return true;
        return false;
    }

    static void Build()
    {
        if (exact != null) return;
        exact = new Dictionary<string, string>(ExactPairs.Length / 2);
        for (int i = 0; i + 1 < ExactPairs.Length; i += 2) exact[ExactPairs[i]] = ExactPairs[i + 1];
        templates = new List<(Regex, string[], string)>();
        var order = new List<int>();
        for (int i = 0; i + 1 < TemplatePairs.Length; i += 2) order.Add(i);
        // 글자가 많은(더 구체적인) 틀부터 맞춰 본다(같으면 표 순서대로 — 정렬이 안정적이지 않아 순서가 바뀔 수 있었다)
        order.Sort((a, b) =>
        {
            int c = Hole.Replace(TemplatePairs[b], "").Length.CompareTo(Hole.Replace(TemplatePairs[a], "").Length);
            return c != 0 ? c : a.CompareTo(b);
        });
        foreach (int i in order)
        {
            string ko = TemplatePairs[i].Trim('\n'), en = TemplatePairs[i + 1].Trim('\n');
            var names = new List<string>();
            var pattern = new StringBuilder("^");
            int last = 0;
            foreach (Match m in Hole.Matches(ko))
            {
                // 숫자 자리(횟수·개수·초·거리)는 숫자만 받는다: 느슨한 틀("{n}회")이 아무 문장 끝의 "회"까지 먹지 않게
                pattern.Append(Regex.Escape(ko.Substring(last, m.Index - last))).Append(NumberHole.IsMatch(m.Value) ? @"(-?[0-9][0-9.,]*)" : "(.+?)");
                names.Add(m.Value);
                last = m.Index + m.Length;
            }
            pattern.Append(Regex.Escape(ko.Substring(last))).Append('$');
            templates.Add((new Regex(pattern.ToString(), RegexOptions.Singleline), names.ToArray(), en));
        }
    }

    static string Translate(string text, int depth)
    {
        if (!HasHangul(text)) return text;
        if (exact.TryGetValue(text, out var en)) return en;
        if (depth > 4) return null;

        // 앞뒤 빈칸·줄바꿈
        string trimmed = text.Trim();
        if (trimmed.Length != text.Length)
        {
            var inner = Translate(trimmed, depth + 1);
            if (inner == null) return null;
            int a = text.IndexOf(trimmed, System.StringComparison.Ordinal);
            return text.Substring(0, a) + inner + text.Substring(a + trimmed.Length);
        }

        // 여러 줄: 줄마다(이어 붙인 목표 + "교전 중" 줄, 편지 글)
        if (text.IndexOf('\n') >= 0) return Pieces(text.Split('\n'), "\n", depth);

        // 색 꼬리표 사이마다
        if (text.IndexOf('<') >= 0 && Tag.IsMatch(text)) return Pieces(Tag.Split(text), "", depth);

        // {…} 자리에 값이 들어간 것(줄 · 꼬리표로 나눈 뒤에: 틀에는 줄바꿈 · 꼬리표가 없다)
        foreach (var (re, names, tmpl) in templates)
        {
            var m = re.Match(text);
            if (!m.Success) continue;
            string s = tmpl;
            for (int g = 0; g < names.Length; g++) s = s.Replace(names[g], En(m.Groups[g + 1].Value));
            return s;
        }

        // "A  ·  B"처럼 이어 붙인 것
        foreach (var sep in Separators)
            if (text.Contains(sep)) return Pieces(text.Split(new[] { sep }, System.StringSplitOptions.None), sep, depth);
        return null;
    }

    static readonly string[] Separators = { "  ·  ", " · ", " / ", "   " };

    static string Pieces(string[] parts, string sep, int depth)
    {
        var sb = new StringBuilder();
        bool any = false;
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(sep);
            var p = parts[i];
            if (p.Length == 0 || p[0] == '<' || !HasHangul(p)) { sb.Append(p); continue; }
            var t = Translate(p, depth + 1);
            if (t == null) { Misses.Add(p); sb.Append(p); }
            else { sb.Append(t); any = true; }
        }
        return any ? sb.ToString() : null;
    }
}
