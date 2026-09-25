using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.History;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Tests.Migration;

public sealed class MigrationTests : IDisposable
{
    private const string BaseSettings = """
        {
          // shared
          "ConnectionStrings": { "Default": "Server=base;Password=b" },
          "Jwt": { "Key": "same-key" },
          "Smtp": { "Password": "" }
        }
        """;

    private const string DevSettings = """
        {
          "ConnectionStrings": { "Default": "Server=dev;Password=d" },
          "Jwt": { "Key": "same-key" }
        }
        """;

    private const string ProdSettings = """
        { "ConnectionStrings": { "Default": "Server=prod;Password=p" } }
        """;

    private readonly TempDirectory _temp = new();
    private readonly UserSecretsStore _store;
    private readonly BackupService _backups;
    private readonly string _projectPath;

    public MigrationTests()
    {
        _store = new UserSecretsStore(_temp.Combine("secrets"));
        _backups = new BackupService(_temp.Combine("backups"));
        _projectPath = _temp.Write("Api/Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        _temp.Write("Api/appsettings.json", BaseSettings);
        _temp.Write("Api/appsettings.Development.json", DevSettings);
        _temp.Write("Api/appsettings.Production.json", ProdSettings);
    }

    public void Dispose() => _temp.Dispose();

    private ProjectConfiguration LoadConfiguration() =>
        ProjectConfiguration.Load(ProjectInspector.Inspect(_projectPath), _store);

    [Fact]
    public void CreatePlan_DefaultsToDevelopmentValue_AndFlagsConflict()
    {
        var plan = new MigrationPlanner(_store).CreatePlan(LoadConfiguration(), ["ConnectionStrings:Default", "Jwt:Key"]);

        var connection = plan.Items.Single(i => i.Key == "ConnectionStrings:Default");
        Assert.True(connection.HasConflict);
        Assert.Equal("Server=dev;Password=d", connection.Selected.Value);
        Assert.Equal(3, connection.Candidates.Count);

        var jwt = plan.Items.Single(i => i.Key == "Jwt:Key");
        Assert.False(jwt.HasConflict);
        Assert.Equal(["appsettings.Development.json", "appsettings.json"], jwt.Candidates[0].Sources);
    }

    [Fact]
    public void CreatePlan_DoesNotClearNonDevelopmentFilesByDefault()
    {
        var plan = new MigrationPlanner(_store).CreatePlan(LoadConfiguration(), ["ConnectionStrings:Default"]);

        var actions = plan.Items[0].FileActions;
        Assert.True(actions.Single(a => a.File.IsBase).IsEnabled);
        Assert.True(actions.Single(a => a.File.IsDevelopment).IsEnabled);
        var prod = actions.Single(a => a.File.Environment == "Production");
        Assert.False(prod.IsEnabled);
        Assert.Contains("ConnectionStrings__Default", prod.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlan_SkipsEmptyKeys_AndPlansNewUserSecretsId()
    {
        var plan = new MigrationPlanner(_store).CreatePlan(LoadConfiguration(), ["Smtp:Password", "Jwt:Key"]);

        Assert.Equal(["Smtp:Password"], plan.SkippedKeys);
        Assert.NotNull(plan.NewUserSecretsId);
        Assert.False(plan.IsBlocked);
    }

    [Fact]
    public void Apply_MovesValues_ThenRestoreUndoesEverything()
    {
        var configuration = LoadConfiguration();
        var plan = new MigrationPlanner(_store).CreatePlan(configuration, ["ConnectionStrings:Default", "Jwt:Key"]);
        var projectBefore = File.ReadAllText(_projectPath);

        var entry = new ChangeSetExecutor(_backups).Apply(ChangeSetFactory.FromPlan(plan));

        var after = LoadConfiguration();
        Assert.Equal(plan.NewUserSecretsId, after.Project.SecretsId.Id);
        Assert.Equal("Server=dev;Password=d", after.Secrets!["ConnectionStrings:Default"]);
        Assert.Equal("same-key", after.Secrets["Jwt:Key"]);
        var baseText = File.ReadAllText(_temp.Combine("Api", "appsettings.json"));
        Assert.Contains("// shared", baseText, StringComparison.Ordinal);
        Assert.Contains("\"Default\": \"\"", baseText, StringComparison.Ordinal);
        Assert.Equal(ProdSettings, File.ReadAllText(_temp.Combine("Api", "appsettings.Production.json")));

        _backups.Restore(entry);

        Assert.Equal(projectBefore, File.ReadAllText(_projectPath));
        Assert.Equal(BaseSettings, File.ReadAllText(_temp.Combine("Api", "appsettings.json")));
        Assert.Equal(DevSettings, File.ReadAllText(_temp.Combine("Api", "appsettings.Development.json")));
        Assert.False(File.Exists(after.SecretsFilePath));
        Assert.Equal(2, _backups.List(_projectPath).Count);
    }

    [Fact]
    public void Apply_Fails_WhenFileChangedAfterPlanning()
    {
        var plan = new MigrationPlanner(_store).CreatePlan(LoadConfiguration(), ["Jwt:Key"]);
        var changes = ChangeSetFactory.FromPlan(plan);
        File.AppendAllText(_temp.Combine("Api", "appsettings.json"), " ");

        Assert.Throws<FileChangedExternallyException>(() => new ChangeSetExecutor(_backups).Apply(changes));
        Assert.Empty(_backups.List());
    }

    [Fact]
    public void MoveBack_WritesSecretIntoTargetFile_AndRemovesSecret()
    {
        var executor = new ChangeSetExecutor(_backups);
        var plan = new MigrationPlanner(_store).CreatePlan(LoadConfiguration(), ["Jwt:Key"]);
        executor.Apply(ChangeSetFactory.FromPlan(plan));

        var configuration = LoadConfiguration();
        var target = configuration.Files.Single(f => f.IsDevelopment);
        executor.Apply(ChangeSetFactory.MoveBack(configuration, ["Jwt:Key"], target, removeFromSecrets: true));

        var after = LoadConfiguration();
        Assert.False(after.Secrets!.ContainsKey("Jwt:Key"));
        Assert.Equal("same-key", after.Files.Single(f => f.IsDevelopment).Document!.Find("Jwt:Key")!.Value);
    }

    [Fact]
    public void ReplaceSecrets_WritesFlatJson()
    {
        var configuration = LoadConfiguration();
        var changes = new ChangeSetFactory(_store).ReplaceSecrets(configuration,
            [new("A:B", "1"), new("Pass", "ş\"x")], "test");

        new ChangeSetExecutor(_backups).Apply(changes);

        var after = LoadConfiguration();
        Assert.Equal("1", after.Secrets!["A:B"]);
        Assert.Equal("ş\"x", after.Secrets["Pass"]);
        Assert.Contains("\"A:B\": \"1\"", File.ReadAllText(after.SecretsFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_IncludesCustomFiles_AndReportsMissingOnes()
    {
        _temp.Write("Api/config/ocelot.json", """{ "GlobalConfiguration": { "ApiKey": "gw-key" } }""");

        var configuration = ProjectConfiguration.Load(ProjectInspector.Inspect(_projectPath), _store,
            ["config/ocelot.json", "missing.json"]);

        var custom = configuration.Files[^1];
        Assert.True(custom.IsCustom);
        Assert.True(custom.AppliesToAllEnvironments);
        Assert.False(custom.IsBase);
        Assert.Equal("ocelot.json", custom.DisplayName);
        Assert.Equal(["missing.json"], configuration.MissingCustomFiles.Select(Path.GetFileName));
        Assert.Contains(configuration.BuildEntries(), e => e.Key == "GlobalConfiguration:ApiKey");
    }

    [Fact]
    public void CreatePlan_ClearsCustomFilesByDefault_WithoutEnvironmentWarning()
    {
        _temp.Write("Api/ocelot.json", """{ "Jwt": { "Key": "gateway-key" } }""");
        var configuration = ProjectConfiguration.Load(ProjectInspector.Inspect(_projectPath), _store, ["ocelot.json"]);

        var plan = new MigrationPlanner(_store).CreatePlan(configuration, ["Jwt:Key"]);

        var custom = plan.Items[0].FileActions.Single(a => a.File.IsCustom);
        Assert.True(custom.IsEnabled);
        Assert.Null(custom.Warning);
        Assert.True(plan.Items[0].HasConflict);
        Assert.Equal("same-key", plan.Items[0].Selected.Value);
    }

    [Fact]
    public void BuildEntries_CombinesFilesAndSecrets()
    {
        var entries = LoadConfiguration().BuildEntries();

        var connection = entries.Single(e => e.Key == "ConnectionStrings:Default");
        Assert.Equal(3, connection.FileValues.Count);
        Assert.NotEqual(Core.Analysis.SensitivityLevel.None, connection.Hint.Level);
        Assert.Contains(entries, e => e.Key == "Smtp:Password" && !e.HasFileContent);
    }
}
