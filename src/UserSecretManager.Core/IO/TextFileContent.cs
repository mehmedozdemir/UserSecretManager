using System.Text;

namespace UserSecretManager.Core.IO;

/// <summary>
/// Text content of a file together with the encoding details needed to write it back unchanged.
/// </summary>
/// <param name="Text">Decoded text without the byte order mark.</param>
/// <param name="HasBom">Whether the original file started with a UTF-8 byte order mark.</param>
public sealed record TextFileContent(string Text, bool HasBom)
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>The newline sequence used by the text; defaults to the platform newline.</summary>
    public string NewLine => DetectNewLine(Text);

    /// <summary>Reads a file, preserving whether it carried a UTF-8 BOM.</summary>
    public static TextFileContent Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return FromBytes(bytes);
    }

    /// <summary>Decodes raw file bytes as UTF-8, detecting a BOM.</summary>
    public static TextFileContent FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        var text = Encoding.UTF8.GetString(hasBom ? bytes.AsSpan(Utf8Bom.Length) : bytes);
        return new TextFileContent(text, hasBom);
    }

    /// <summary>Encodes the text back to bytes, restoring the BOM if the original had one.</summary>
    public byte[] ToBytes()
    {
        var body = Encoding.UTF8.GetBytes(Text);
        if (!HasBom)
        {
            return body;
        }

        var result = new byte[Utf8Bom.Length + body.Length];
        Utf8Bom.CopyTo(result, 0);
        body.CopyTo(result, Utf8Bom.Length);
        return result;
    }

    /// <summary>Returns a copy with different text but the same encoding details.</summary>
    public TextFileContent WithText(string text) => this with { Text = text };

    internal static string DetectNewLine(string text)
    {
        var index = text.IndexOf('\n', StringComparison.Ordinal);
        if (index < 0)
        {
            return Environment.NewLine;
        }

        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }
}
