using UserSecretManager.Core.Analysis;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.IO;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Tests.Configuration;

public class SmallComponentTests
{
    [Theory]
    [InlineData("appsettings.json", true, null)]
    [InlineData("appsettings.Development.json", true, "Development")]
    [InlineData("AppSettings.Production.EU.json", true, "Production.EU")]
    [InlineData("appsettings.json.bak", false, null)]
    [InlineData("settings.json", false, null)]
    public void TryGetEnvironment_RecognizesAppSettingsFiles(string name, bool expected, string? environment)
    {
        Assert.Equal(expected, AppSettingsFile.TryGetEnvironment(name, out var env));
        Assert.Equal(environment, env);
    }

    [Fact]
    public void TextFileContent_RoundTripsBom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'];

        var content = TextFileContent.FromBytes(bytes);

        Assert.True(content.HasBom);
        Assert.Equal("{}", content.Text);
        Assert.Equal(bytes, content.ToBytes());
    }

    [Fact]
    public void LineDiff_ShowsChangedLinesWithContext()
    {
        var oldText = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line{i}"));
        var newText = oldText.Replace("line10", "LINE10", StringComparison.Ordinal);

        var diff = LineDiff.Compute(oldText, newText, context: 1);

        Assert.Equal(
            [DiffLineKind.Gap, DiffLineKind.Unchanged, DiffLineKind.Removed, DiffLineKind.Added, DiffLineKind.Unchanged, DiffLineKind.Gap],
            diff.Select(d => d.Kind).ToArray());
        Assert.Equal(10, diff[2].OldNumber);
    }

    [Theory]
    [InlineData("ConnectionStrings:Default", "Server=.;Database=x", SensitivityLevel.High)]
    [InlineData("Jwt:Key", "whatever", SensitivityLevel.High)]
    [InlineData("Smtp:Password", "x", SensitivityLevel.High)]
    [InlineData("Auth:ClientSecret", "x", SensitivityLevel.High)]
    [InlineData("Api:AccessToken", "x", SensitivityLevel.High)]
    [InlineData("Db:Main", "Host=h;Username=u;Password=p", SensitivityLevel.High)]
    [InlineData("Proxy:Url", "http://user:pass@proxy:8080", SensitivityLevel.High)]
    [InlineData("Jwt:TokenLifetime", "30", SensitivityLevel.None)]
    [InlineData("KeyVault:Uri", "https://kv.vault.azure.net", SensitivityLevel.None)]
    [InlineData("Features:ApiKey", "true", SensitivityLevel.None)]
    [InlineData("AzureAd:TenantId", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", SensitivityLevel.None)]
    [InlineData("Logging:LogLevel:Default", "Information", SensitivityLevel.None)]
    [InlineData("Jwt:Key", "", SensitivityLevel.None)]
    public void SensitivityAnalyzer_ClassifiesKeys(string key, string value, SensitivityLevel expected)
    {
        Assert.Equal(expected, SensitivityAnalyzer.Analyze(key, [value]).Level);
    }

    [Fact]
    public void UserSecretsIdWriter_AddsToFirstUnconditionalPropertyGroup()
    {
        var project = """
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <DefineConstants>X</DefineConstants>
              </PropertyGroup>

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

            </Project>
            """;

        var result = UserSecretsIdWriter.AddUserSecretsId(project, "my-id");

        var expected = project.Replace(
            "<TargetFramework>net10.0</TargetFramework>\n",
            "<TargetFramework>net10.0</TargetFramework>\n    <UserSecretsId>my-id</UserSecretsId>\n",
            StringComparison.Ordinal);
        Assert.Equal(expected.Replace("\r\n", "\n", StringComparison.Ordinal), result.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void UserSecretsIdWriter_CreatesPropertyGroupWhenMissing()
    {
        var result = UserSecretsIdWriter.AddUserSecretsId("<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>", "id1");

        Assert.Contains("<PropertyGroup>\n    <UserSecretsId>id1</UserSecretsId>\n  </PropertyGroup>", result, StringComparison.Ordinal);
    }
}
