using UserSecretManager.Core.Discovery;

namespace UserSecretManager.Core.Tests.Discovery;

public class DiscoveryTests
{
    [Fact]
    public void SolutionReader_ReadsSlnAndSlnx()
    {
        using var temp = new TempDirectory();
        temp.Write("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        temp.Write("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var sln = temp.Write("App.sln", """
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Api", "src\Api\Api.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "folder", "folder", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);
        var slnx = temp.Write("App.slnx", """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Api/Api.csproj" />
                <Project Path="src/Lib/Lib.csproj" />
                <Project Path="src/Missing/Missing.csproj" />
              </Folder>
            </Solution>
            """);

        Assert.Equal(["Api.csproj"], SolutionReader.GetProjectPaths(sln).Select(Path.GetFileName));
        Assert.Equal(["Api.csproj", "Lib.csproj"], SolutionReader.GetProjectPaths(slnx).Select(Path.GetFileName));
    }

    [Fact]
    public void Inspect_ReadsIdFromProjectFile_AndFindsConfigFiles()
    {
        using var temp = new TempDirectory();
        var project = temp.Write("Api/Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><UserSecretsId>abc-123</UserSecretsId></PropertyGroup>
            </Project>
            """);
        temp.Write("Api/appsettings.json", "{}");
        temp.Write("Api/appsettings.Production.json", "{}");
        temp.Write("Api/appsettings.Development.json", "{}");
        temp.Write("Api/Properties/launchSettings.json", """
            { "profiles": { "http": { "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" } },
                            "stg": { "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Staging" } } } }
            """);

        var info = ProjectInspector.Inspect(project);

        Assert.Equal("abc-123", info.SecretsId.Id);
        Assert.Equal(UserSecretsIdSource.ProjectFile, info.SecretsId.Source);
        Assert.True(info.HasUserSecretsSupport);
        Assert.Equal(3, info.ConfigFilePaths.Count);
        Assert.Equal("appsettings.json", Path.GetFileName(info.ConfigFilePaths[0]));
        Assert.Equal(["Development", "Staging"], info.LaunchEnvironments);
    }

    [Fact]
    public void Inspect_FallsBackToDirectoryBuildProps_ThenAssemblyAttribute()
    {
        using var temp = new TempDirectory();
        temp.Write("Directory.Build.props", "<Project><PropertyGroup><UserSecretsId>shared-id</UserSecretsId></PropertyGroup></Project>");
        var fromProps = temp.Write("A/A.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        using var other = new TempDirectory();
        var fromAttribute = other.Write("B/B.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        other.Write("B/Props.cs", "[assembly: Microsoft.Extensions.Configuration.UserSecrets.UserSecretsId(\"attr-id\")]");

        Assert.Equal(("shared-id", UserSecretsIdSource.DirectoryBuildProps),
            (ProjectInspector.Inspect(fromProps).SecretsId.Id, ProjectInspector.Inspect(fromProps).SecretsId.Source));
        var attr = ProjectInspector.Inspect(fromAttribute).SecretsId;
        Assert.Equal(("attr-id", UserSecretsIdSource.AssemblyAttribute), (attr.Id, attr.Source));
        Assert.False(ProjectInspector.Inspect(fromAttribute).HasUserSecretsSupport);
    }

    [Fact]
    public void Inspect_MarksMsBuildExpressionAsUnresolvable()
    {
        using var temp = new TempDirectory();
        var project = temp.Write("A/A.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UserSecretsId>$(Company)-api</UserSecretsId></PropertyGroup></Project>");

        var id = ProjectInspector.Inspect(project).SecretsId;

        Assert.Equal(UserSecretsIdSource.Unresolvable, id.Source);
        Assert.False(id.CanWrite);
    }
}
