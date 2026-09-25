using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.IO;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Migration;

/// <summary>Turns user intentions (a migration plan, secret edits, moving back) into concrete file changes.</summary>
public sealed class ChangeSetFactory
{
    private readonly UserSecretsStore _store;

    /// <summary>Creates a factory.</summary>
    public ChangeSetFactory(UserSecretsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Changes that apply a migration plan.</summary>
    public static ChangeSet FromPlan(MigrationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.IsBlocked)
        {
            throw new InvalidOperationException("Plan uygulanamaz durumda.");
        }

        var configuration = plan.Configuration;
        var changes = new List<FileChange>();
        if (plan.NewUserSecretsId is { } newId)
        {
            changes.Add(AddIdToProject(configuration.Project, newId));
        }

        var secrets = configuration.Secrets!.Clone();
        foreach (var item in plan.Items)
        {
            secrets[item.Key] = item.Selected.Value;
        }

        AddIfChanged(changes, SecretsChange(plan.SecretsFilePath, configuration.Secrets!, secrets));

        var clearsByFile = plan.Items
            .SelectMany(i => i.FileActions.Where(a => a.IsEnabled).Select(a => (a.File, i.Key)))
            .GroupBy(x => x.File);
        foreach (var group in clearsByFile)
        {
            var values = group.ToDictionary(x => x.Key, _ => string.Empty, ConfigKey.Comparer);
            AddIfChanged(changes, AppSettingsChange(group.Key, values));
        }

        var description = $"{plan.Items.Count} anahtar user secrets'a taşındı: {Summarize(plan.Items.Select(i => i.Key))}";
        return new ChangeSet(configuration.Project.ProjectPath, configuration.Project.Name, description, changes);
    }

    /// <summary>
    /// Changes that replace the project's secrets with <paramref name="secrets"/> (add, edit, delete in one go).
    /// Adds a <c>UserSecretsId</c> to the project when it has none.
    /// </summary>
    public ChangeSet ReplaceSecrets(ProjectConfiguration configuration, IEnumerable<KeyValuePair<string, string>> secrets,
        string description)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secrets);
        EnsureSecretsWritable(configuration);

        var changes = new List<FileChange>();
        var path = configuration.SecretsFilePath;
        if (configuration.Project.SecretsId.Source == UserSecretsIdSource.None)
        {
            var newId = UserSecretsIdWriter.NewId();
            changes.Add(AddIdToProject(configuration.Project, newId));
            path = _store.GetFilePath(newId);
        }

        var updated = new SecretCollection(path, configuration.Secrets!.Exists, configuration.Secrets.RawText);
        foreach (var pair in secrets)
        {
            updated[pair.Key] = pair.Value;
        }

        AddIfChanged(changes, SecretsChange(path, configuration.Secrets, updated));
        return new ChangeSet(configuration.Project.ProjectPath, configuration.Project.Name, description, changes);
    }

    /// <summary>
    /// Changes that write secret values back into <paramref name="target"/> and optionally delete them from secrets.
    /// </summary>
    public static ChangeSet MoveBack(ProjectConfiguration configuration, IReadOnlyCollection<string> keys, AppSettingsFile target,
        bool removeFromSecrets)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(target);
        EnsureSecretsWritable(configuration);

        var secrets = configuration.Secrets!;
        var values = keys
            .Where(secrets.ContainsKey)
            .ToDictionary(k => k, k => secrets[k], ConfigKey.Comparer);
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Seçilen anahtarlar user secrets içinde bulunamadı.");
        }

        var changes = new List<FileChange>();
        AddIfChanged(changes, AppSettingsChange(target, values));
        if (removeFromSecrets)
        {
            var remaining = secrets.Clone();
            foreach (var key in values.Keys)
            {
                remaining.Remove(key);
            }

            AddIfChanged(changes, SecretsChange(configuration.SecretsFilePath, secrets, remaining));
        }

        var description = $"{values.Count} secret {target.FileName} dosyasına geri taşındı: {Summarize(values.Keys)}";
        return new ChangeSet(configuration.Project.ProjectPath, configuration.Project.Name, description, changes);
    }

    /// <summary>Changes that create or update <c>secrets.template.json</c> with the current secret keys.</summary>
    public static ChangeSet WriteTemplate(ProjectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Secrets is not { Count: > 0 } secrets)
        {
            throw new InvalidOperationException("Şablon için en az bir secret gerekir.");
        }

        var path = Transfer.SecretsTemplate.PathFor(configuration.Project.Directory);
        var existing = File.Exists(path) ? TextFileContent.Read(path) : null;
        var text = Transfer.SecretsTemplate.Create(secrets.Keys);
        var content = existing?.WithText(text) ?? new TextFileContent(text, HasBom: false);
        var changes = new List<FileChange>();
        AddIfChanged(changes, new FileChange(path, FileChangeKind.Template, Transfer.SecretsTemplate.FileName, existing?.Text, content));
        return new ChangeSet(configuration.Project.ProjectPath, configuration.Project.Name,
            $"{Transfer.SecretsTemplate.FileName} güncellendi ({secrets.Count} anahtar)", changes);
    }

    private static void EnsureSecretsWritable(ProjectConfiguration configuration)
    {
        if (configuration.Secrets is null)
        {
            throw new InvalidOperationException($"secrets.json okunamadı: {configuration.SecretsError}");
        }

        if (!configuration.Project.SecretsId.CanWrite)
        {
            throw new InvalidOperationException("UserSecretsId çözümlenemediği için secret'lar yazılamaz.");
        }
    }

    private static FileChange AddIdToProject(ProjectInfo project, string newId)
    {
        var content = TextFileContent.Read(project.ProjectPath);
        var updated = UserSecretsIdWriter.AddUserSecretsId(content.Text, newId);
        return new FileChange(project.ProjectPath, FileChangeKind.ProjectFile, Path.GetFileName(project.ProjectPath),
            content.Text, content.WithText(updated));
    }

    private static FileChange SecretsChange(string path, SecretCollection original, SecretCollection updated) =>
        new(path, FileChangeKind.Secrets, "secrets.json", original.RawText, new TextFileContent(updated.ToJson(), HasBom: false));

    private static FileChange AppSettingsChange(AppSettingsFile file, IReadOnlyDictionary<string, string> values)
    {
        if (file.Document is null)
        {
            throw new InvalidOperationException($"{file.FileName} geçerli bir JSON değil: {file.ParseError}");
        }

        var text = file.Document.WithValues(values);
        return new FileChange(file.Path, FileChangeKind.AppSettings, file.FileName, file.Content.Text, file.Content.WithText(text));
    }

    private static void AddIfChanged(List<FileChange> changes, FileChange change)
    {
        if (!string.Equals(change.OriginalText, change.NewContent.Text, StringComparison.Ordinal))
        {
            changes.Add(change);
        }
    }

    private static string Summarize(IEnumerable<string> keys)
    {
        const int MaxListed = 5;
        var list = keys.ToList();
        var shown = string.Join(", ", list.Take(MaxListed));
        return list.Count > MaxListed ? $"{shown} (+{list.Count - MaxListed})" : shown;
    }
}
