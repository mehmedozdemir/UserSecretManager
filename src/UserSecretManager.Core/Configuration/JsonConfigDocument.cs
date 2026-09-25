using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UserSecretManager.Core.Configuration;

/// <summary>
/// A JSON configuration document (comments and trailing commas allowed) that exposes its values as flattened
/// configuration keys and can be edited without touching anything except the edited values, so comments,
/// indentation and ordering are preserved.
/// </summary>
public sealed class JsonConfigDocument
{
    private const int DefaultIndentSize = 2;

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions ValueSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly byte[] _utf8;
    private readonly List<JsonConfigValue> _values;
    private readonly Dictionary<string, ObjectSpan> _objects;
    private readonly HashSet<string> _containers;

    private JsonConfigDocument(string text, byte[] utf8, List<JsonConfigValue> values,
        Dictionary<string, ObjectSpan> objects, HashSet<string> containers)
    {
        Text = text;
        _utf8 = utf8;
        _values = values;
        _objects = objects;
        _containers = containers;
    }

    /// <summary>The original text of the document.</summary>
    public string Text { get; }

    /// <summary>Every leaf value in document order. Duplicate keys appear once per occurrence.</summary>
    public IReadOnlyList<JsonConfigValue> Values => _values;

    /// <summary>
    /// Parses <paramref name="text"/>. Throws <see cref="JsonException"/> when the text is not valid JSON or its root is
    /// not an object.
    /// </summary>
    public static JsonConfigDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var utf8 = Encoding.UTF8.GetBytes(text);
        var scanner = new Scanner(utf8);
        scanner.Run();
        return new JsonConfigDocument(text, utf8, scanner.Values, scanner.Objects, scanner.Containers);
    }

    /// <summary>Tries to parse; returns the error message on failure.</summary>
    public static bool TryParse(string text, out JsonConfigDocument? document, out string? error)
    {
        try
        {
            document = Parse(text);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Returns the effective value of <paramref name="key"/> (the last occurrence wins) or <c>null</c>.</summary>
    public JsonConfigValue? Find(string key) =>
        _values.LastOrDefault(v => ConfigKey.Comparer.Equals(v.Key, key));

    /// <summary>Whether <paramref name="key"/> exists as a leaf value.</summary>
    public bool Contains(string key) => Find(key) is not null;

    /// <summary>
    /// Sets existing leaf values to the given strings (as JSON strings) and inserts keys that do not exist yet.
    /// Only the affected characters change.
    /// </summary>
    /// <exception cref="InvalidOperationException">A key collides with an existing non-object value.</exception>
    public string WithValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var replacements = new List<(int Start, int End, byte[] Bytes)>();
        var missing = new List<KeyValuePair<string, string>>();
        foreach (var pair in values)
        {
            var matches = _values.Where(v => ConfigKey.Comparer.Equals(v.Key, pair.Key)).ToList();
            if (matches.Count == 0)
            {
                missing.Add(pair);
                continue;
            }

            var literal = Encoding.UTF8.GetBytes(ToJsonString(pair.Value));
            replacements.AddRange(matches.Select(m => (m.Start, m.End, literal)));
        }

        var text = ApplyReplacements(replacements);
        foreach (var pair in missing)
        {
            text = Parse(text).Insert(pair.Key, pair.Value);
        }

        return text;
    }

    /// <summary>Encodes a string as a JSON string literal without escaping non-ASCII characters.</summary>
    public static string ToJsonString(string value) => JsonSerializer.Serialize(value, ValueSerializerOptions);

    private string ApplyReplacements(List<(int Start, int End, byte[] Bytes)> replacements)
    {
        if (replacements.Count == 0)
        {
            return Text;
        }

        var buffer = new List<byte>(_utf8);
        foreach (var (start, end, bytes) in replacements.OrderByDescending(r => r.Start))
        {
            buffer.RemoveRange(start, end - start);
            buffer.InsertRange(start, bytes);
        }

        return Encoding.UTF8.GetString([.. buffer]);
    }

    private string Insert(string key, string value)
    {
        var segments = ConfigKey.Split(key);
        var parentLength = FindDeepestExistingObject(segments, key);
        var parentPath = string.Join(ConfigKey.Delimiter, segments.Take(parentLength));
        var remaining = segments.Skip(parentLength).ToArray();
        var parent = _objects[parentPath];

        var newLine = IO.TextFileContent.DetectNewLine(Text);
        var indentUnit = DetectIndentUnit();
        var closeIndent = IndentOfLineAt(parent.Close);
        var memberIndent = parent.FirstMemberStart is { } first && IsFirstOnLine(first)
            ? IndentOfLineAt(first)
            : closeIndent + indentUnit;

        var property = BuildProperty(remaining, value, memberIndent, indentUnit, newLine);
        string inserted;
        int insertAt;
        if (parent.LastMemberEnd is { } lastEnd)
        {
            insertAt = lastEnd;
            inserted = "," + newLine + memberIndent + property;
        }
        else
        {
            insertAt = parent.Close;
            var between = Encoding.UTF8.GetString(_utf8, parent.Open + 1, parent.Close - parent.Open - 1);
            if (string.IsNullOrWhiteSpace(between))
            {
                var prefix = Encoding.UTF8.GetString(_utf8, 0, parent.Open + 1);
                var suffix = Encoding.UTF8.GetString(_utf8, parent.Close, _utf8.Length - parent.Close);
                return prefix + newLine + memberIndent + property + newLine + closeIndent + suffix;
            }

            inserted = newLine + memberIndent + property + newLine + closeIndent;
        }

        var head = Encoding.UTF8.GetString(_utf8, 0, insertAt);
        var tail = Encoding.UTF8.GetString(_utf8, insertAt, _utf8.Length - insertAt);
        return head + inserted + tail;
    }

    private int FindDeepestExistingObject(string[] segments, string key)
    {
        var deepest = 0;
        for (var length = 1; length < segments.Length; length++)
        {
            var path = string.Join(ConfigKey.Delimiter, segments.Take(length));
            if (_objects.ContainsKey(path))
            {
                deepest = length;
                continue;
            }

            if (_containers.Contains(path) || _values.Any(v => ConfigKey.Comparer.Equals(v.Key, path)))
            {
                throw new InvalidOperationException(
                    $"'{key}' eklenemiyor: '{path}' nesne olmayan bir değer olarak zaten tanımlı.");
            }

            break;
        }

        return deepest;
    }

    private static string BuildProperty(string[] segments, string value, string indent, string unit, string newLine)
    {
        var name = ToJsonString(segments[0]);
        if (segments.Length == 1)
        {
            return $"{name}: {ToJsonString(value)}";
        }

        var innerIndent = indent + unit;
        var inner = BuildProperty(segments[1..], value, innerIndent, unit, newLine);
        return $"{name}: {{{newLine}{innerIndent}{inner}{newLine}{indent}}}";
    }

    private string IndentOfLineAt(int byteIndex)
    {
        var lineStart = byteIndex;
        while (lineStart > 0 && _utf8[lineStart - 1] != (byte)'\n')
        {
            lineStart--;
        }

        var end = lineStart;
        while (end < _utf8.Length && (_utf8[end] == (byte)' ' || _utf8[end] == (byte)'\t'))
        {
            end++;
        }

        return Encoding.UTF8.GetString(_utf8, lineStart, end - lineStart);
    }

    private bool IsFirstOnLine(int byteIndex)
    {
        for (var i = byteIndex - 1; i >= 0; i--)
        {
            var b = _utf8[i];
            if (b == (byte)'\n')
            {
                return true;
            }

            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r')
            {
                return false;
            }
        }

        return true;
    }

    private string DetectIndentUnit()
    {
        foreach (var span in _objects.Values)
        {
            if (span.FirstMemberStart is not { } first || !IsFirstOnLine(first))
            {
                continue;
            }

            var member = IndentOfLineAt(first);
            var owner = IndentOfLineAt(span.Close);
            if (member.Length > owner.Length && member.StartsWith(owner, StringComparison.Ordinal))
            {
                return member[owner.Length..];
            }
        }

        return new string(' ', DefaultIndentSize);
    }

    private sealed record ObjectSpan(int Open, int Close, int? FirstMemberStart, int? LastMemberEnd);

    /// <summary>Walks the token stream once and records leaf values and object boundaries.</summary>
    private sealed class Scanner(byte[] utf8)
    {
        private readonly Stack<Frame> _frames = new();
        private string? _pendingProperty;
        private int _pendingPropertyStart;

        public List<JsonConfigValue> Values { get; } = [];

        public Dictionary<string, ObjectSpan> Objects { get; } = new(ConfigKey.Comparer);

        public HashSet<string> Containers { get; } = new(ConfigKey.Comparer);

        public void Run()
        {
            var reader = new Utf8JsonReader(utf8, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Yapılandırma dosyasının kökü bir JSON nesnesi olmalıdır.");
            }

            _frames.Push(new Frame(string.Empty, isArray: false, (int)reader.TokenStartIndex));
            while (reader.Read())
            {
                Visit(ref reader);
            }
        }

        private void Visit(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    _pendingProperty = reader.GetString();
                    _pendingPropertyStart = (int)reader.TokenStartIndex;
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    var path = NextChildPath(out _);
                    Containers.Add(path);
                    _frames.Push(new Frame(path, reader.TokenType == JsonTokenType.StartArray,
                        (int)reader.TokenStartIndex));
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    CloseFrame((int)reader.TokenStartIndex);
                    break;
                default:
                    AddValue(ref reader);
                    break;
            }
        }

        private void CloseFrame(int closeIndex)
        {
            var frame = _frames.Pop();
            if (!frame.IsArray)
            {
                Objects[frame.Path] = new ObjectSpan(frame.Open, closeIndex, frame.FirstMemberStart, frame.LastMemberEnd);
            }

            if (_frames.TryPeek(out var parent))
            {
                parent.LastMemberEnd = closeIndex + 1;
            }
        }

        private void AddValue(ref Utf8JsonReader reader)
        {
            var key = NextChildPath(out var parent);
            var start = (int)reader.TokenStartIndex;
            var end = (int)reader.BytesConsumed;
            var (value, kind) = reader.TokenType switch
            {
                JsonTokenType.String => (reader.GetString(), JsonValueKind.String),
                JsonTokenType.Number => (Encoding.UTF8.GetString(reader.ValueSpan), JsonValueKind.Number),
                JsonTokenType.True => ("true", JsonValueKind.True),
                JsonTokenType.False => ("false", JsonValueKind.False),
                _ => ((string?)null, JsonValueKind.Null),
            };

            Values.Add(new JsonConfigValue(key, value, kind, start, end));
            parent.LastMemberEnd = end;
        }

        private string NextChildPath(out Frame parent)
        {
            parent = _frames.Peek();
            string segment;
            if (parent.IsArray)
            {
                segment = parent.NextIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                parent.NextIndex++;
            }
            else
            {
                segment = _pendingProperty ?? throw new JsonException("Beklenmeyen JSON yapısı.");
                parent.FirstMemberStart ??= _pendingPropertyStart;
                _pendingProperty = null;
            }

            return ConfigKey.Combine(parent.Path, segment);
        }

        private sealed class Frame(string path, bool isArray, int open)
        {
            public string Path { get; } = path;

            public bool IsArray { get; } = isArray;

            public int Open { get; } = open;

            public int NextIndex { get; set; }

            public int? FirstMemberStart { get; set; }

            public int? LastMemberEnd { get; set; }
        }
    }
}

/// <summary>A leaf value of a JSON configuration document.</summary>
/// <param name="Key">Flattened configuration key.</param>
/// <param name="Value">Value as configuration sees it; <c>null</c> for JSON null.</param>
/// <param name="Kind">Original JSON kind.</param>
/// <param name="Start">UTF-8 byte offset where the value token starts.</param>
/// <param name="End">UTF-8 byte offset just after the value token.</param>
public sealed record JsonConfigValue(string Key, string? Value, JsonValueKind Kind, int Start, int End)
{
    /// <summary>Whether the value carries content worth protecting.</summary>
    public bool HasContent => !string.IsNullOrEmpty(Value);
}
