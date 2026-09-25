using System.Text.Json;
using UserSecretManager.Core.Configuration;

namespace UserSecretManager.Core.Tests.Configuration;

public class JsonConfigDocumentTests
{
    private const string Sample = """
        {
          // Database settings
          "ConnectionStrings": {
            "Default": "Server=.;Password=p@ss;", /* inline */
            "Cache": "localhost:6379"
          },
          "Jwt": { "Key": "abc", "Lifetime": 30, "Enabled": true },
          "Hosts": [ "a", "b" ],
          "Empty": null,
          "Trailing": "x",
        }
        """;

    [Fact]
    public void Parse_FlattensKeysLikeConfiguration()
    {
        var doc = JsonConfigDocument.Parse(Sample);

        var keys = doc.Values.Select(v => v.Key).ToArray();
        Assert.Equal(
            ["ConnectionStrings:Default", "ConnectionStrings:Cache", "Jwt:Key", "Jwt:Lifetime", "Jwt:Enabled",
             "Hosts:0", "Hosts:1", "Empty", "Trailing"],
            keys);
        Assert.Equal("30", doc.Find("jwt:lifetime")!.Value);
        Assert.Equal("true", doc.Find("Jwt:Enabled")!.Value);
        Assert.Null(doc.Find("Empty")!.Value);
    }

    [Fact]
    public void WithValues_ReplacesOnlyTargetValues_PreservingCommentsAndLayout()
    {
        var doc = JsonConfigDocument.Parse(Sample);

        var result = doc.WithValues(new Dictionary<string, string>
        {
            ["ConnectionStrings:Default"] = "",
            ["Jwt:Lifetime"] = "",
        });

        var expected = Sample
            .Replace("\"Server=.;Password=p@ss;\"", "\"\"", StringComparison.Ordinal)
            .Replace("\"Lifetime\": 30", "\"Lifetime\": \"\"", StringComparison.Ordinal);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void WithValues_HandlesMultiByteCharacters()
    {
        var doc = JsonConfigDocument.Parse("{ \"Açıklama\": \"şğü\", \"Parola\": \"çok gizli\" }");

        var result = doc.WithValues(new Dictionary<string, string> { ["Parola"] = "" });

        Assert.Equal("{ \"Açıklama\": \"şğü\", \"Parola\": \"\" }", result);
    }

    [Fact]
    public void WithValues_InsertsMissingNestedKey_UsingDocumentIndentation()
    {
        var text = "{\r\n    \"Logging\": {\r\n        \"Level\": \"Info\"\r\n    }\r\n}";
        var doc = JsonConfigDocument.Parse(text);

        var result = doc.WithValues(new Dictionary<string, string>
        {
            ["Logging:Secret"] = "s",
            ["Smtp:Auth:Password"] = "p",
        });

        var expected =
            "{\r\n    \"Logging\": {\r\n        \"Level\": \"Info\",\r\n        \"Secret\": \"s\"\r\n    },\r\n" +
            "    \"Smtp\": {\r\n        \"Auth\": {\r\n            \"Password\": \"p\"\r\n        }\r\n    }\r\n}";
        Assert.Equal(expected, result);
        Assert.Equal("p", JsonConfigDocument.Parse(result).Find("Smtp:Auth:Password")!.Value);
    }

    [Fact]
    public void WithValues_InsertsIntoEmptyObject()
    {
        var doc = JsonConfigDocument.Parse("{}");

        var result = doc.WithValues(new Dictionary<string, string> { ["A:B"] = "1" });

        Assert.Equal("1", JsonConfigDocument.Parse(result).Find("A:B")!.Value);
    }

    [Fact]
    public void WithValues_Throws_WhenParentIsNotAnObject()
    {
        var doc = JsonConfigDocument.Parse("{ \"A\": \"x\" }");

        Assert.Throws<InvalidOperationException>(() =>
            doc.WithValues(new Dictionary<string, string> { ["A:B"] = "1" }));
    }

    [Fact]
    public void WithValues_EscapesSpecialCharacters()
    {
        var doc = JsonConfigDocument.Parse("{ \"A\": \"\" }");

        var result = doc.WithValues(new Dictionary<string, string> { ["A"] = "q\"uote\\back\nline" });

        Assert.Equal("q\"uote\\back\nline", JsonConfigDocument.Parse(result).Find("A")!.Value);
    }

    [Fact]
    public void Parse_RejectsNonObjectRoot()
    {
        Assert.ThrowsAny<JsonException>(() => JsonConfigDocument.Parse("[1,2]"));
    }
}
