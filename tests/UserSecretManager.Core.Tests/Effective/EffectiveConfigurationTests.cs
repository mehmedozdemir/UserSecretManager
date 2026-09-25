using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Effective;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Tests.Effective;

public sealed class EffectiveConfigurationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UserSecretsStore _store;
    private readonly string _projectPath;

    public EffectiveConfigurationTests()
    {
        _store = new UserSecretsStore(_temp.Combine("secrets"));
        _projectPath = _temp.Write("Api/Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><UserSecretsId>eff-id</UserSecretsId></PropertyGroup></Project>");
        _temp.Write("Api/appsettings.json", """
            { "ConnectionStrings": { "Default": "" }, "Logging": { "Level": "Info" }, "Feature": "base" }
            """);
        _temp.Write("Api/appsettings.Development.json", """{ "Logging": { "Level": "Debug" } }""");
        _temp.Write("Api/appsettings.Production.json", """{ "Feature": "prod" }""");
        _temp.Write("Api/ocelot.json", """{ "Feature": "gateway" }""");
        _temp.Write("Api/Properties/launchSettings.json", """
            {
              // comments are allowed
              "profiles": {
                "http": { "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development", "Logging__Level": "Trace" } },
                "prod": { "environmentVariables": { "DOTNET_ENVIRONMENT": "Production" } },
                "docker": { "commandName": "Docker" }
              }
            }
            """);
        _temp.Write("secrets/eff-id/secrets.json", """{ "ConnectionStrings:Default": "Server=dev" }""");
    }

    public void Dispose() => _temp.Dispose();

    private ProjectConfiguration Load(params string[] customFiles) =>
        ProjectConfiguration.Load(ProjectInspector.Inspect(_projectPath), _store, customFiles);

    [Fact]
    public void LaunchSettingsReader_ReadsProfilesAndEnvironments()
    {
        var info = ProjectInspector.Inspect(_projectPath);

        Assert.Equal(["http", "prod", "docker"], info.LaunchProfiles.Select(p => p.Name));
        Assert.Equal("Production", info.LaunchProfiles[1].Environment);
        Assert.Null(info.LaunchProfiles[2].Environment);
        Assert.Equal(["Development", "Production"], info.LaunchEnvironments);
    }

    [Fact]
    public void Development_UsesSecretsOverEmptyBaseValue()
    {
        var entries = EffectiveConfigurationBuilder.Build(Load(), new EffectiveConfigurationOptions("Development", true));

        var connection = entries.Single(e => e.Key == "ConnectionStrings:Default");
        Assert.Equal("Server=dev", connection.Value);
        Assert.Equal(EffectiveConfigurationBuilder.UserSecretsSource, connection.Winner.Source);
        Assert.Equal(["appsettings.json", "User Secrets"], connection.Layers.Select(l => l.Source));
        Assert.Equal("Debug", entries.Single(e => e.Key == "Logging:Level").Value);
    }

    [Fact]
    public void Production_WithoutSecrets_ReportsEmptyValues()
    {
        var entries = EffectiveConfigurationBuilder.Build(Load(), new EffectiveConfigurationOptions("production", false));

        var connection = entries.Single(e => e.Key == "ConnectionStrings:Default");
        Assert.True(connection.IsEmpty);
        Assert.Equal("prod", entries.Single(e => e.Key == "Feature").Value);
        Assert.Equal("Info", entries.Single(e => e.Key == "Logging:Level").Value);
    }

    [Fact]
    public void LaunchProfileVariables_AndCustomFiles_AreAppliedInOrder()
    {
        var configuration = Load("ocelot.json");
        var profile = configuration.Project.LaunchProfiles[0];

        var entries = EffectiveConfigurationBuilder.Build(configuration,
            new EffectiveConfigurationOptions("Development", true, profile));

        var level = entries.Single(e => e.Key == "Logging:Level");
        Assert.Equal("Trace", level.Value);
        Assert.Equal("launchSettings: http", level.Winner.Source);
        Assert.Equal(ConfigLayerKind.LaunchProfile, level.Winner.Kind);
        var feature = entries.Single(e => e.Key == "Feature");
        Assert.Equal("gateway", feature.Value);
        Assert.Equal(ConfigLayerKind.CustomFile, feature.Winner.Kind);
        Assert.True(feature.IsOverridden);
    }
}
