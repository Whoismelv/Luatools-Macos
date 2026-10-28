using System.Text;

namespace LuaTools.Core;

/// <summary>
/// Valve KeyValues (VDF/ACF) reader. Produces nested dictionaries of string -> object
/// where object is either string or Dictionary&lt;string, object&gt;.
/// </summary>
public sealed class Vdf
{
    private readonly string _text;
    private int _pos;

    private Vdf(string text) => _text = text;

    public static Dictionary<string, object> ParseFile(string path) =>
        Parse(System.IO.File.ReadAllText(path, Encoding.UTF8));

    public static Dictionary<string, object> Parse(string text)
    {
        var reader = new Vdf(text);
        reader.SkipTrivia();
        if (reader.Peek() == 0xFEFF) reader._pos++;

        var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        reader.ParseBody(root);
        return root;
    }

    private void ParseBody(Dictionary<string, object> target)
    {
        while (_pos < _text.Length)
        {
            var key = ReadToken();
            if (key is null) return;
            if (key == "}") return;
            if (key == "{") continue;   // stray brace — tolerate

            var value = ReadToken();
            if (value is null) return;

            if (value == "{")
            {
                var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                ParseBody(child);
                target[key] = child;
                continue;
            }

            if (value == "}") return;
            target[key] = value;
        }
    }

    private string? ReadToken()
    {
        SkipTrivia();
        if (_pos >= _text.Length) return null;

        var c = _text[_pos];
        if (c == '{' || c == '}') { _pos++; return c.ToString(); }

        if (c == '"')
        {
            _pos++;
            var sb = new StringBuilder();
            while (_pos < _text.Length)
            {
                var ch = _text[_pos++];
                if (ch == '\\' && _pos < _text.Length)
                {
                    var esc = _text[_pos++];
                    sb.Append(esc switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        '\\' => '\\',
                        '"' => '"',
                        _ => esc
                    });
                    continue;
                }
                if (ch == '"') break;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        // Bracket-delimited platform key: [win]x86
        if (c == '[')
        {
            var sb = new StringBuilder();
            while (_pos < _text.Length && _text[_pos] != ']') sb.Append(_text[_pos++]);
            if (_pos < _text.Length) sb.Append(_text[_pos++]);
            while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos]))
                sb.Append(_text[_pos++]);
            return sb.ToString();
        }

        var bare = new StringBuilder();
        while (_pos < _text.Length && !char.IsWhiteSpace(_text[_pos])
               && _text[_pos] != '{' && _text[_pos] != '}' && _text[_pos] != '"')
            bare.Append(_text[_pos++]);

        return bare.Length == 0 ? null : bare.ToString();
    }

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            var c = _text[_pos];
            if (char.IsWhiteSpace(c)) { _pos++; continue; }
            if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
            {
                while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
                continue;
            }
            break;
        }
    }

    private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

    public static string? Str(Dictionary<string, object> map, string key) =>
        map.TryGetValue(key, out var v) ? v as string : null;

    public static Dictionary<string, object>? Map(Dictionary<string, object> map, string key) =>
        map.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
}
