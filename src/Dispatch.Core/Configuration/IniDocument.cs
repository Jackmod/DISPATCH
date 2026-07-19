using System.Text;

namespace Dispatch.Core.Configuration;

/// <summary>
/// An ini file held as the lines it is made of, so a value can be changed
/// without disturbing anything else.
/// </summary>
/// <remarks>
/// This is a line model, not a parser that rebuilds the file from a
/// dictionary. Mod config files carry comments explaining what each setting
/// does, blank lines for grouping, a particular key order, and sometimes a
/// specific encoding — and a mod update adds keys the app has never heard of.
/// Regenerating from a template silently discards all of that. Every method
/// here mutates a value in place and leaves every other byte untouched, which
/// is the difference between an edit the user can trust and one that quietly
/// eats their customisation.
///
/// <para>
/// Comparisons are case-insensitive for both sections and keys, because these
/// files disagree about casing and a person editing one does not expect
/// <c>CallSign</c> and <c>Callsign</c> to be different settings.
/// </para>
/// </remarks>
public sealed class IniDocument
{
    private readonly List<Line> _lines = [];
    private readonly Encoding _encoding;
    private readonly bool _hadBom;
    private readonly string _newline;

    private IniDocument(Encoding encoding, bool hadBom, string newline)
    {
        _encoding = encoding;
        _hadBom = hadBom;
        _newline = newline;
    }

    /// <summary>The encoding the file was read in, preserved for writing.</summary>
    public Encoding Encoding => _encoding;

    /// <summary>Parses ini text into a line model.</summary>
    public static IniDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // CRLF if the file has any, matching what Windows tools write; a lone
        // LF file keeps LF. Never rewrite line endings as a side effect.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var document = new IniDocument(new UTF8Encoding(false), hadBom: false, newline);
        document.Load(text);
        return document;
    }

    /// <summary>Reads an ini file, detecting and preserving its encoding.</summary>
    public static async Task<IniDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var (encoding, hadBom) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, hadBom ? PreambleLength(bytes) : 0, bytes.Length - (hadBom ? PreambleLength(bytes) : 0));

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var document = new IniDocument(encoding, hadBom, newline);
        document.Load(text);
        return document;
    }

    /// <summary>Renders the document back to text, byte-for-byte where unchanged.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();

        for (var i = 0; i < _lines.Count; i++)
        {
            builder.Append(_lines[i].Raw);

            // The final line keeps its original terminator state: a file that
            // ended without a trailing newline still does.
            if (i < _lines.Count - 1 || _endsWithNewline)
            {
                builder.Append(_newline);
            }
        }

        return builder.ToString();
    }

    /// <summary>Writes the document back to disk, preserving encoding and BOM.</summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = ToText();
        var body = _encoding.GetBytes(text);

        byte[] bytes;
        if (_hadBom)
        {
            var preamble = _encoding.GetPreamble();
            bytes = new byte[preamble.Length + body.Length];
            preamble.CopyTo(bytes, 0);
            body.CopyTo(bytes, preamble.Length);
        }
        else
        {
            bytes = body;
        }

        // Temp-and-move, so a crash mid-write cannot truncate a config file
        // the user may have hand-tuned.
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads a value, or null when the key is absent.</summary>
    public string? Get(string section, string key)
    {
        var index = FindValueLine(section, key);
        return index >= 0 ? _lines[index].Value : null;
    }

    /// <summary>True when the section and key both exist.</summary>
    public bool Has(string section, string key) => FindValueLine(section, key) >= 0;

    /// <summary>
    /// Sets a value, adding the section or key if needed. Returns true if the
    /// file changed.
    /// </summary>
    /// <remarks>
    /// A no-op write — setting a value to what it already is — returns false
    /// and touches nothing, so the staged-changes diff never lists a line that
    /// did not actually move.
    /// </remarks>
    public bool Set(string section, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var index = FindValueLine(section, key);

        if (index >= 0)
        {
            if (string.Equals(_lines[index].Value, value, StringComparison.Ordinal))
            {
                return false;
            }

            _lines[index] = _lines[index].WithValue(value);
            return true;
        }

        InsertKey(section, key, value);
        return true;
    }

    /// <summary>Every key in a section, in file order.</summary>
    public IReadOnlyList<string> KeysIn(string section)
    {
        var keys = new List<string>();
        var inSection = IsRootSection(section);

        foreach (var line in _lines)
        {
            if (line.Kind == LineKind.Section)
            {
                inSection = SectionEquals(line.Section, section);
            }
            else if (inSection && line.Kind == LineKind.KeyValue && line.Key is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>Every section name, in file order. The root section is the empty string.</summary>
    public IReadOnlyList<string> Sections =>
        _lines.Where(l => l.Kind == LineKind.Section)
              .Select(l => l.Section!)
              .ToList();

    private bool _endsWithNewline = true;

    private void Load(string text)
    {
        _endsWithNewline = text.Length == 0 || text.EndsWith('\n');

        // Split on either terminator without keeping empty trailing segments
        // from the final newline.
        var raw = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var count = _endsWithNewline && raw.Length > 0 ? raw.Length - 1 : raw.Length;

        string currentSection = string.Empty;

        for (var i = 0; i < count; i++)
        {
            var line = Line.Classify(raw[i], currentSection);
            if (line.Kind == LineKind.Section)
            {
                currentSection = line.Section!;
            }

            _lines.Add(line);
        }
    }

    private int FindValueLine(string section, string key)
    {
        var inSection = IsRootSection(section);

        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];

            if (line.Kind == LineKind.Section)
            {
                inSection = SectionEquals(line.Section, section);
            }
            else if (inSection && line.Kind == LineKind.KeyValue && KeyEquals(line.Key, key))
            {
                return i;
            }
        }

        return -1;
    }

    private void InsertKey(string section, string key, string value)
    {
        var newLine = Line.KeyValueLine(key, value);

        if (IsRootSection(section))
        {
            // Root keys go before the first section header, keeping them in
            // the section they belong to rather than trailing the file.
            var firstSection = _lines.FindIndex(l => l.Kind == LineKind.Section);
            var at = firstSection >= 0 ? firstSection : _lines.Count;
            _lines.Insert(LastNonBlankBefore(at) + 1, newLine);
            return;
        }

        var sectionIndex = _lines.FindIndex(l =>
            l.Kind == LineKind.Section && SectionEquals(l.Section, section));

        if (sectionIndex < 0)
        {
            // A missing section is created at the end, after a blank separator
            // if the file does not already end on one.
            if (_lines.Count > 0 && _lines[^1].Kind != LineKind.Blank)
            {
                _lines.Add(Line.BlankLine());
            }

            _lines.Add(Line.SectionLine(section));
            _lines.Add(newLine);
            return;
        }

        // Insert after the last key already in the section, before the blank
        // line or next header that follows it.
        var insertAt = sectionIndex + 1;
        for (var i = sectionIndex + 1; i < _lines.Count; i++)
        {
            if (_lines[i].Kind == LineKind.Section)
            {
                break;
            }

            if (_lines[i].Kind == LineKind.KeyValue)
            {
                insertAt = i + 1;
            }
        }

        _lines.Insert(insertAt, newLine);
    }

    private int LastNonBlankBefore(int limit)
    {
        for (var i = Math.Min(limit, _lines.Count) - 1; i >= 0; i--)
        {
            if (_lines[i].Kind != LineKind.Blank)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsRootSection(string section) => string.IsNullOrEmpty(section);

    private static bool SectionEquals(string? a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool KeyEquals(string? a, string b) =>
        string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int PreambleLength(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return 3;
        }

        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
        {
            return 2;
        }

        return 0;
    }

    private static (Encoding Encoding, bool HadBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), true);
        }

        // No BOM: UTF-8 without one, which is what the overwhelming majority of
        // these files are.
        return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false);
    }
}
