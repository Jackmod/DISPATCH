namespace Dispatch.Core.Configuration;

/// <summary>What a line in an ini file is.</summary>
internal enum LineKind
{
    /// <summary>Empty or whitespace only.</summary>
    Blank,

    /// <summary>A comment, starting with ; or #.</summary>
    Comment,

    /// <summary>A <c>[section]</c> header.</summary>
    Section,

    /// <summary>A <c>key = value</c> pair.</summary>
    KeyValue,

    /// <summary>Anything that does not parse as the above, kept verbatim.</summary>
    Other,
}

/// <summary>
/// One physical line, holding both its meaning and its exact original text.
/// </summary>
/// <remarks>
/// The <see cref="Raw"/> string is the source of truth for anything unchanged,
/// so a line the app never touches is written back exactly as it arrived —
/// including whatever spacing, alignment or inline comment it carried. Only a
/// value that is actually changed is re-rendered, and even then the key,
/// spacing and separator around it are preserved.
/// </remarks>
internal sealed record Line
{
    private Line(LineKind kind, string raw)
    {
        Kind = kind;
        Raw = raw;
    }

    /// <summary>What kind of line this is.</summary>
    public LineKind Kind { get; private init; }

    /// <summary>The exact original text, minus the line terminator.</summary>
    public string Raw { get; private init; }

    /// <summary>Section name, for a section header.</summary>
    public string? Section { get; private init; }

    /// <summary>Key, for a key-value line.</summary>
    public string? Key { get; private init; }

    /// <summary>Value, for a key-value line.</summary>
    public string? Value { get; private init; }

    /// <summary>Text before the value, kept so re-rendering preserves spacing.</summary>
    private string Prefix { get; init; } = string.Empty;

    /// <summary>Text after the value — trailing space or an inline comment.</summary>
    private string Suffix { get; init; } = string.Empty;

    /// <summary>Classifies a raw line within the current section.</summary>
    public static Line Classify(string raw, string currentSection)
    {
        var trimmed = raw.TrimStart();

        if (trimmed.Length == 0)
        {
            return new Line(LineKind.Blank, raw);
        }

        if (trimmed[0] is ';' or '#')
        {
            return new Line(LineKind.Comment, raw);
        }

        if (trimmed[0] == '[')
        {
            var close = raw.IndexOf(']');
            if (close > 0)
            {
                var name = raw[(raw.IndexOf('[') + 1)..close].Trim();
                return new Line(LineKind.Section, raw) { Section = name };
            }
        }

        var equals = raw.IndexOf('=');
        if (equals > 0)
        {
            var key = raw[..equals];
            var afterEquals = raw[(equals + 1)..];

            // Split the value from any trailing whitespace or inline comment,
            // so both survive a re-render.
            var (value, suffix) = SplitValue(afterEquals);

            return new Line(LineKind.KeyValue, raw)
            {
                Key = key.Trim(),
                Value = value,
                Prefix = raw[..(equals + 1)] + LeadingWhitespace(afterEquals),
                Suffix = suffix,
            };
        }

        return new Line(LineKind.Other, raw);
    }

    /// <summary>Builds a new key-value line, formatted as <c>key = value</c>.</summary>
    public static Line KeyValueLine(string key, string value) =>
        new(LineKind.KeyValue, $"{key} = {value}")
        {
            Key = key,
            Value = value,
            Prefix = $"{key} = ",
            Suffix = string.Empty,
        };

    /// <summary>Builds a section header line.</summary>
    public static Line SectionLine(string name) =>
        new(LineKind.Section, $"[{name}]") { Section = name };

    /// <summary>Builds a blank line.</summary>
    public static Line BlankLine() => new(LineKind.Blank, string.Empty);

    /// <summary>Returns a copy with a new value, re-rendering only the value portion.</summary>
    public Line WithValue(string value)
    {
        if (Kind != LineKind.KeyValue)
        {
            throw new InvalidOperationException("Only a key-value line has a value.");
        }

        return this with
        {
            Value = value,
            Raw = Prefix + value + Suffix,
        };
    }

    private static (string Value, string Suffix) SplitValue(string afterEquals)
    {
        var trimmed = afterEquals.TrimStart();

        // An inline comment starts at an unquoted ; or #. Most mod files do not
        // use them, but the ones that do rely on them surviving.
        var commentAt = -1;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] is ';' or '#')
            {
                commentAt = i;
                break;
            }
        }

        if (commentAt < 0)
        {
            var value = trimmed.TrimEnd();
            var suffix = trimmed[value.Length..];
            return (value, suffix);
        }

        var beforeComment = trimmed[..commentAt];
        var valuePart = beforeComment.TrimEnd();
        var betweenAndComment = beforeComment[valuePart.Length..] + trimmed[commentAt..];
        return (valuePart, betweenAndComment);
    }

    private static string LeadingWhitespace(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        return text[..i];
    }
}
