using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.History;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Secrets;
using UserSecretManager.Core.Transfer;

namespace UserSecretManager.Core.Tests.Transfer;

public class TransferTests
{
    private const int FastIterations = 1_000;

    private static readonly SecretsArchiveContent Sample = new("Shop.Api", "id-1",
        new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero),
        [new("ConnectionStrings:Default", "Server=.;Password=ş€crét"), new("Jwt:Key", "k")]);

    [Fact]
    public void Archive_RoundTrips_AndHidesValues()
    {
        var data = SecretsArchive.Export(Sample, "correct horse", FastIterations);

        var text = System.Text.Encoding.UTF8.GetString(data);
        Assert.DoesNotContain("Password", text, StringComparison.Ordinal);
        Assert.Contains("usm-secrets", text, StringComparison.Ordinal);
        var imported = SecretsArchive.Import(data, "correct horse");
        Assert.Equal(Sample.Secrets, imported.Secrets);
        Assert.Equal(("Shop.Api", "id-1", Sample.ExportedAt), (imported.ProjectName, imported.UserSecretsId, imported.ExportedAt));
    }

    [Fact]
    public void Archive_RejectsWrongPassword_TamperingAndForeignFiles()
    {
        var data = SecretsArchive.Export(Sample, "correct horse", FastIterations);
        var tampered = System.Text.Encoding.UTF8.GetString(data)
            .Replace($"\"Iterations\": {FastIterations}", $"\"Iterations\": {FastIterations + 1}", StringComparison.Ordinal);

        Assert.Throws<SecretsArchiveException>(() => SecretsArchive.Import(data, "wrong horse"));
        Assert.Throws<SecretsArchiveException>(() => SecretsArchive.Import(System.Text.Encoding.UTF8.GetBytes(tampered), "correct horse"));
        Assert.Throws<SecretsArchiveException>(() => SecretsArchive.Import("{ \"a\": 1 }"u8.ToArray(), "correct horse"));
        Assert.Throws<SecretsArchiveException>(() => SecretsArchive.Import("not json"u8.ToArray(), "correct horse"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("short", false)]
    [InlineData("long enough", true)]
    public void ValidatePassword_RequiresMinimumLength(string? password, bool valid)
    {
        Assert.Equal(valid, SecretsArchive.ValidatePassword(password) is null);
        if (!valid)
        {
            Assert.Throws<ArgumentException>(() => SecretsArchive.Export(Sample, password!, FastIterations));
        }
    }

    [Fact]
    public void Template_ContainsSortedKeysWithEmptyValues()
    {
        var text = SecretsTemplate.Create(["Jwt:Key", "ConnectionStrings:Default", "jwt:key"]);

        Assert.Equal(["ConnectionStrings:Default", "Jwt:Key"], SecretsTemplate.ReadKeys(text));
        Assert.DoesNotContain("\": \"x", text, StringComparison.Ordinal);
        Assert.Contains("\"Jwt:Key\": \"\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteTemplate_CreatesFileNextToProject_WithBackup()
    {
        using var temp = new TempDirectory();
        var store = new UserSecretsStore(temp.Combine("secrets"));
        var project = temp.Write("Api/Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><UserSecretsId>tpl</UserSecretsId></PropertyGroup></Project>");
        temp.Write("secrets/tpl/secrets.json", """{ "B": "2", "A": "1" }""");
        var configuration = ProjectConfiguration.Load(ProjectInspector.Inspect(project), store);

        var changes = ChangeSetFactory.WriteTemplate(configuration);
        new ChangeSetExecutor(new BackupService(temp.Combine("backups"))).Apply(changes);

        var path = temp.Combine("Api", SecretsTemplate.FileName);
        Assert.Equal(["A", "B"], SecretsTemplate.ReadKeys(File.ReadAllText(path)));
        Assert.True(ChangeSetFactory.WriteTemplate(ProjectConfiguration.Load(ProjectInspector.Inspect(project), store)).IsEmpty);
    }
}
