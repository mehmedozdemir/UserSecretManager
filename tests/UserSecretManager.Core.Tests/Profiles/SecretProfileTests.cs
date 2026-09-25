using System.Security.Cryptography;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Profiles;
using UserSecretManager.Core.Secrets;
using UserSecretManager.Core.Security;

namespace UserSecretManager.Core.Tests.Profiles;

public sealed class SecretProfileTests : IDisposable
{
    private const string Id = "profile-id";

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private SecretProfileStore CreateStore(ISecretProtector? protector = null) =>
        new(_temp.Combine("profiles"), protector ?? new KeyFileSecretProtector(_temp.Combine("key.bin")));

    public static TheoryData<string> Protectors => OperatingSystem.IsWindows() ? ["dpapi", "keyfile"] : ["keyfile"];

    [Theory]
    [MemberData(nameof(Protectors))]
    public void SaveAndLoad_RoundTripsValues_WithoutStoringPlaintext(string protectorName)
    {
        ISecretProtector protector = protectorName == "dpapi" && OperatingSystem.IsWindows()
            ? new DpapiSecretProtector()
            : new KeyFileSecretProtector(_temp.Combine("key.bin"));
        var store = CreateStore(protector);

        store.Save(Id, "Staging DB", [new("ConnectionStrings:Default", "Server=stg;Password=çok-gizli"), new("Jwt:Key", "k")], "stg");

        var loaded = store.Load(Id, "staging db");
        Assert.Equal("Server=stg;Password=çok-gizli", loaded[0].Value);
        Assert.Equal(["ConnectionStrings:Default", "Jwt:Key"], loaded.Select(v => v.Key));
        var info = Assert.Single(store.List(Id));
        Assert.Equal(("Staging DB", "stg", 2), (info.Name, info.Description, info.KeyCount));
        var raw = File.ReadAllText(Directory.GetFiles(_temp.Combine("profiles", Id))[0]);
        Assert.DoesNotContain("çok-gizli", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_Overwrites_KeepingCreationTime()
    {
        var store = CreateStore();
        var first = store.Save(Id, "Local", [new("A", "1")]);

        var second = store.Save(Id, "LOCAL", [new("A", "2"), new("B", "3")]);

        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Single(store.List(Id));
        Assert.Equal("2", store.Load(Id, "Local")[0].Value);
    }

    [Fact]
    public void Rename_RejectsDuplicates_AndDeleteRemoves()
    {
        var store = CreateStore();
        store.Save(Id, "One", [new("A", "1")]);
        store.Save(Id, "Two", [new("A", "2")]);

        Assert.Throws<InvalidOperationException>(() => store.Rename(Id, "One", "two"));
        store.Rename(Id, "One", "Uno");
        store.Delete(Id, "Two");

        Assert.Equal(["Uno"], store.List(Id).Select(p => p.Name));
        Assert.Equal("1", store.Load(Id, "Uno")[0].Value);
    }

    [Fact]
    public void Load_FailsWithOtherKey()
    {
        CreateStore().Save(Id, "Local", [new("A", "1")]);
        File.WriteAllBytes(_temp.Combine("key.bin"), RandomNumberGenerator.GetBytes(32));

        Assert.ThrowsAny<CryptographicException>(() => CreateStore().Load(Id, "Local"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Staging / EU", true)]
    public void ValidateName_AcceptsAnyVisibleName(string name, bool valid)
    {
        Assert.Equal(valid, SecretProfileStore.ValidateName(name) is null);
    }

    [Fact]
    public void Matches_ComparesKeysAndValues()
    {
        var secrets = new SecretCollection("x", true, null) { ["A"] = "1", ["B"] = "2" };

        Assert.True(SecretProfileStore.Matches([new("b", "2"), new("A", "1")], secrets));
        Assert.False(SecretProfileStore.Matches([new("A", "1")], secrets));
        Assert.False(SecretProfileStore.Matches([new("A", "1"), new("B", "x")], secrets));
    }

    [Fact]
    public void Compare_ReportsEveryKeyWithItsEffect()
    {
        var current = new SecretCollection("x", true, null) { ["Same"] = "1", ["Diff"] = "old", ["OnlyCurrent"] = "c" };
        KeyValuePair<string, string>[] profile = [new("Same", "1"), new("diff", "new"), new("New", "n")];

        var replace = ProfileValues.Compare(current, profile, ProfileApplyMode.Replace);
        var merge = ProfileValues.Compare(current, profile, ProfileApplyMode.Merge);

        Assert.Equal(
            [SecretChangeKind.Unchanged, SecretChangeKind.Changed, SecretChangeKind.Added, SecretChangeKind.Removed],
            replace.Select(c => c.Kind));
        Assert.Equal(("old", "new"), (replace[1].CurrentValue, replace[1].NewValue));
        Assert.Null(replace[3].NewValue);
        Assert.Equal(SecretChangeKind.Kept, merge[3].Kind);
        Assert.Equal("c", merge[3].NewValue);
    }

    [Fact]
    public void Compare_ProfileEqualToSecrets_IsAllUnchanged()
    {
        var current = new SecretCollection("x", true, null) { ["A"] = "1", ["B"] = "2" };

        var changes = ProfileValues.Compare(current, current.ToList(), ProfileApplyMode.Replace);

        Assert.All(changes, c => Assert.Equal(SecretChangeKind.Unchanged, c.Kind));
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void ProfileValues_FromEnvironment_AndApplyModes()
    {
        var store = new UserSecretsStore(_temp.Combine("secrets"));
        var project = _temp.Write("Api/Api.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><UserSecretsId>pv</UserSecretsId></PropertyGroup></Project>");
        _temp.Write("Api/appsettings.json", """{ "Db": "", "Jwt": { "Key": "base-key" } }""");
        _temp.Write("Api/appsettings.Staging.json", """{ "Db": "Server=stg" }""");
        _temp.Write("secrets/pv/secrets.json", """{ "Db": "Server=dev", "Jwt:Key": "dev-key", "Only:Local": "x" }""");
        var configuration = ProjectConfiguration.Load(ProjectInspector.Inspect(project), store);

        var staging = ProfileValues.FromEnvironment(configuration, "Staging", configuration.Secrets!.Keys);

        Assert.Equal([new("Db", "Server=stg"), new("Jwt:Key", "base-key")], staging.Values);
        Assert.Equal(["Only:Local"], staging.MissingKeys);
        Assert.Equal(2, ProfileValues.Apply(configuration.Secrets, staging.Values, ProfileApplyMode.Replace).Count);
        var merged = ProfileValues.Apply(configuration.Secrets, staging.Values, ProfileApplyMode.Merge);
        Assert.Equal([new("Db", "Server=stg"), new("Jwt:Key", "base-key"), new("Only:Local", "x")], merged);
    }
}
