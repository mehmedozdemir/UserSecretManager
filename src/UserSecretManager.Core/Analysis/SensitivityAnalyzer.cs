using System.Text.RegularExpressions;
using UserSecretManager.Core.Configuration;

namespace UserSecretManager.Core.Analysis;

/// <summary>How confident the analyzer is that a value is sensitive.</summary>
public enum SensitivityLevel
{
    /// <summary>Nothing suspicious.</summary>
    None,

    /// <summary>Looks like it may be sensitive.</summary>
    Medium,

    /// <summary>Very likely sensitive.</summary>
    High,
}

/// <summary>Why a key was suggested.</summary>
/// <param name="Level">Confidence.</param>
/// <param name="Reason">Human readable explanation.</param>
public sealed record SensitivityHint(SensitivityLevel Level, string Reason)
{
    /// <summary>No suggestion.</summary>
    public static SensitivityHint None { get; } = new(SensitivityLevel.None, string.Empty);
}

/// <summary>
/// Suggests which configuration keys are likely to hold secrets. Suggestions only; the user decides.
/// </summary>
public static partial class SensitivityAnalyzer
{
    private const int MinOpaqueTokenLength = 32;

    private static readonly HashSet<string> SensitiveLastWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "pwd", "secret", "secrets", "token", "key", "credential", "credentials",
        "connectionstring", "passphrase", "apikey", "pat", "salt", "pin",
    };

    private static readonly HashSet<string> SensitiveSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConnectionStrings",
    };

    /// <summary>Analyzes a key and the values it has across files.</summary>
    public static SensitivityHint Analyze(string key, IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(values);

        var nonEmpty = values.Where(v => !string.IsNullOrWhiteSpace(v)).Cast<string>().ToList();
        if (nonEmpty.Count == 0)
        {
            return SensitivityHint.None;
        }

        var valueHint = nonEmpty.Select(AnalyzeValue).MaxBy(h => h.Level) ?? SensitivityHint.None;
        if (valueHint.Level == SensitivityLevel.High)
        {
            return valueHint;
        }

        var keyHint = AnalyzeKey(key, nonEmpty);
        return keyHint.Level >= valueHint.Level ? keyHint : valueHint;
    }

    private static SensitivityHint AnalyzeKey(string key, List<string> values)
    {
        if (values.TrueForAll(IsTrivialValue))
        {
            return SensitivityHint.None;
        }

        var segments = ConfigKey.Split(key);
        if (segments.Length > 1 && SensitiveSections.Contains(segments[0]))
        {
            return new SensitivityHint(SensitivityLevel.High, "Connection string bölümü");
        }

        var words = SplitWords(segments[^1]);
        if (words.Count == 0)
        {
            return SensitivityHint.None;
        }

        var last = words[^1];
        var lastTwo = words.Count > 1 ? words[^2] + last : last;
        if (SensitiveLastWords.Contains(lastTwo) || SensitiveLastWords.Contains(last))
        {
            return new SensitivityHint(SensitivityLevel.High, $"Anahtar adı '{segments[^1]}' hassas veri çağrıştırıyor");
        }

        return words.Any(w => w is "password" or "secret" or "token" or "credential")
            ? new SensitivityHint(SensitivityLevel.Medium, $"Anahtar adında '{segments[^1]}' geçiyor")
            : SensitivityHint.None;
    }

    private static SensitivityHint AnalyzeValue(string value)
    {
        if (CredentialInConnectionString().IsMatch(value))
        {
            return new SensitivityHint(SensitivityLevel.High, "Değer parola/anahtar içeren bir connection string");
        }

        if (CredentialInUrl().IsMatch(value))
        {
            return new SensitivityHint(SensitivityLevel.High, "URL içinde kullanıcı adı ve parola var");
        }

        if (value.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return new SensitivityHint(SensitivityLevel.High, "Değer bir özel anahtar/sertifika");
        }

        if (JwtPattern().IsMatch(value))
        {
            return new SensitivityHint(SensitivityLevel.High, "Değer bir JWT");
        }

        var looksOpaque = value.Length >= MinOpaqueTokenLength && OpaqueTokenPattern().IsMatch(value) &&
                          value.Any(char.IsDigit) && value.Any(char.IsLetter) && !Guid.TryParse(value, out _);
        return looksOpaque
            ? new SensitivityHint(SensitivityLevel.Medium, "Değer rastgele bir anahtara benziyor")
            : SensitivityHint.None;
    }

    private static bool IsTrivialValue(string value) =>
        bool.TryParse(value, out _) ||
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    internal static List<string> SplitWords(string segment) =>
        WordPattern().Matches(segment).Select(m => m.Value.ToLowerInvariant()).ToList();

    [GeneratedRegex(@"[A-Z]{2,}(?=[A-Z][a-z]|\b|[^A-Za-z]|$)|[A-Z]?[a-z]+|[A-Z]+|\d+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?:^|;)\s*(?:password|pwd|accountkey|sharedaccesskey|sharedaccesssignature|client\s*secret|access\s*key)\s*=\s*[^;\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialInConnectionString();

    [GeneratedRegex(@"^[a-z][a-z0-9+.-]*://[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialInUrl();

    [GeneratedRegex(@"^eyJ[\w-]+\.[\w-]+\.[\w-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"^[A-Za-z0-9+/=_\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueTokenPattern();
}
