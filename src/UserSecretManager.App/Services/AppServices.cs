using UserSecretManager.Core.History;
using UserSecretManager.Core.IO;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Profiles;
using UserSecretManager.Core.Secrets;
using UserSecretManager.Core.Security;
using UserSecretManager.Core.Workspace;

namespace UserSecretManager.App.Services;

/// <summary>Composition root: the Core services the view models share.</summary>
public sealed class AppServices
{
    public AppServices(AppDataLocation location, IDialogService dialogs, IPlatformService platform)
    {
        Location = location;
        Dialogs = dialogs;
        Platform = platform;
        SecretsStore = new UserSecretsStore();
        Backups = new BackupService(location.BackupsDirectory);
        Executor = new ChangeSetExecutor(Backups);
        ChangeSets = new ChangeSetFactory(SecretsStore);
        Planner = new MigrationPlanner(SecretsStore);
        Workspace = new WorkspaceStore(location.WorkspaceFile);
        Profiles = new SecretProfileStore(location.ProfilesDirectory, SecretProtectors.CreateDefault(location));
    }

    public AppDataLocation Location { get; }

    public IDialogService Dialogs { get; }

    public IPlatformService Platform { get; }

    public UserSecretsStore SecretsStore { get; }

    public BackupService Backups { get; }

    public ChangeSetExecutor Executor { get; }

    public ChangeSetFactory ChangeSets { get; }

    public MigrationPlanner Planner { get; }

    public WorkspaceStore Workspace { get; }

    public SecretProfileStore Profiles { get; }
}
