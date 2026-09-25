using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.Core.Migration;

/// <summary>
/// Builds a <see cref="MigrationPlan"/> for the keys the user selected.
/// </summary>
/// <remarks>
/// Rules (see docs/ANALYSIS.md): the default secret value is the value that is effective in Development
/// (existing secret &gt; Development file &gt; base file &gt; other environments); differing values are a conflict the user
/// resolves; files of environments other than Development are not cleared unless the user opts in.
/// </remarks>
public sealed class MigrationPlanner
{
    private const string SecretsSourceLabel = "User Secrets";

    private readonly UserSecretsStore _store;

    /// <summary>Creates a planner that resolves secrets file paths with <paramref name="store"/>.</summary>
    public MigrationPlanner(UserSecretsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Creates the plan.</summary>
    public MigrationPlan CreatePlan(ProjectConfiguration configuration, IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keys);

        var notices = new List<PlanNotice>();
        var project = configuration.Project;
        var newId = project.SecretsId.Source == UserSecretsIdSource.None ? UserSecretsIdWriter.NewId() : null;
        var secretsPath = project.SecretsId.Id is { } id ? _store.GetFilePath(id)
            : newId is not null ? _store.GetFilePath(newId)
            : string.Empty;

        var items = new List<MigrationItem>();
        var skipped = new List<string>();
        foreach (var key in keys.Distinct(ConfigKey.Comparer))
        {
            var item = CreateItem(configuration, key);
            if (item is null)
            {
                skipped.Add(key);
            }
            else
            {
                items.Add(item);
            }
        }

        AddProjectNotices(configuration, newId, notices);
        AddItemNotices(configuration, items, skipped, notices);
        return new MigrationPlan(configuration, items, skipped, newId, secretsPath, notices);
    }

    private static MigrationItem? CreateItem(ProjectConfiguration configuration, string key)
    {
        var occurrences = configuration.ValidFiles
            .Select(file => (File: file, Value: file.Document!.Find(key)?.Value))
            .Where(o => !string.IsNullOrEmpty(o.Value))
            .Select(o => (o.File, Value: o.Value!))
            .ToList();

        string? existingSecret = null;
        var hasSecret = configuration.Secrets?.TryGetValue(key, out existingSecret!) == true &&
                        !string.IsNullOrEmpty(existingSecret);

        if (occurrences.Count == 0 && !hasSecret)
        {
            return null;
        }

        var ranked = new List<(int Rank, string Value, string Source, bool IsSecret)>();
        if (hasSecret)
        {
            ranked.Add((0, existingSecret!, SecretsSourceLabel, true));
        }

        ranked.AddRange(occurrences.Select(o => (Rank(o.File), o.Value, o.File.FileName, false)));

        var candidates = ranked
            .OrderBy(r => r.Rank)
            .GroupBy(r => r.Value, StringComparer.Ordinal)
            .Select(g => new ValueCandidate(g.Key, g.Select(r => r.Source).ToList(), g.Any(r => r.IsSecret)))
            .ToList();

        var actions = occurrences
            .Select(o => new FileClearAction(o.File, o.Value, IsClearedByDefault(o.File), WarningFor(o.File, key)))
            .ToList();

        return new MigrationItem(key, candidates, candidates[0], actions);
    }

    private static int Rank(AppSettingsFile file) => file.IsDevelopment ? 1 : file.AppliesToAllEnvironments ? 2 : 3;

    private static bool IsClearedByDefault(AppSettingsFile file) => file.AppliesToAllEnvironments || file.IsDevelopment;

    private static string? WarningFor(AppSettingsFile file, string key)
    {
        if (file.AppliesToAllEnvironments || file.IsDevelopment)
        {
            return null;
        }

        return $"'{file.Environment}' ortamında user secrets yüklenmez. Temizlerseniz değeri bu ortamda " +
               $"'{ConfigKey.ToEnvironmentVariableName(key)}' ortam değişkeni veya bir secret vault ile sağlamalısınız.";
    }

    private static void AddProjectNotices(ProjectConfiguration configuration, string? newId, List<PlanNotice> notices)
    {
        var project = configuration.Project;
        switch (project.SecretsId.Source)
        {
            case UserSecretsIdSource.Unresolvable:
                notices.Add(new PlanNotice(NoticeSeverity.Error,
                    $"UserSecretsId bir MSBuild ifadesiyle tanımlı ('{project.SecretsId.RawValue}') ve çözümlenemiyor."));
                break;
            case UserSecretsIdSource.None:
                notices.Add(new PlanNotice(NoticeSeverity.Info,
                    $"Projede UserSecretsId yok. '{Path.GetFileName(project.ProjectPath)}' dosyasına '{newId}' eklenecek."));
                break;
            case UserSecretsIdSource.DirectoryBuildProps:
                notices.Add(new PlanNotice(NoticeSeverity.Info,
                    "UserSecretsId Directory.Build.props dosyasından geliyor; bu id'yi kullanan tüm projeler aynı secret'ları görür."));
                break;
        }

        if (configuration.SecretsError is not null)
        {
            notices.Add(new PlanNotice(NoticeSeverity.Error, $"secrets.json okunamadı: {configuration.SecretsError}"));
        }

        if (!project.HasUserSecretsSupport)
        {
            notices.Add(new PlanNotice(NoticeSeverity.Warning,
                "Projede user secrets desteği tespit edilemedi (Web SDK, Microsoft.Extensions.Hosting veya " +
                "Microsoft.Extensions.Configuration.UserSecrets). Uygulamanızın AddUserSecrets çağırdığından emin olun."));
        }

        if (project.GitRoot is not null)
        {
            notices.Add(new PlanNotice(NoticeSeverity.Info,
                "Proje bir git reposunda. Taşınan değerler git geçmişinde kalmaya devam eder; bu değerleri değiştirmeniz (rotate) önerilir."));
        }

        foreach (var file in configuration.Files.Where(f => f.Document is null))
        {
            notices.Add(new PlanNotice(NoticeSeverity.Warning, $"{file.FileName} okunamadı ve atlandı: {file.ParseError}"));
        }
    }

    private static void AddItemNotices(ProjectConfiguration configuration, List<MigrationItem> items,
        List<string> skipped, List<PlanNotice> notices)
    {
        if (skipped.Count > 0)
        {
            notices.Add(new PlanNotice(NoticeSeverity.Info,
                $"Değeri boş olduğu için atlanan anahtarlar: {string.Join(", ", skipped)}"));
        }

        var conflicts = items.Count(i => i.HasConflict);
        if (conflicts > 0)
        {
            notices.Add(new PlanNotice(NoticeSeverity.Warning,
                $"{conflicts} anahtarın dosyalarda farklı değerleri var. Varsayılan olarak Development'ta etkin olan değer seçildi; kontrol edin."));
        }

        var clearsBase = items.Any(i => i.FileActions.Any(a => a.File.AppliesToAllEnvironments));
        var hasOtherEnvironments = configuration.Files.Any(f => f.Environment is not null && !f.IsDevelopment) ||
                                   configuration.Project.LaunchEnvironments.Any(e => !AppSettingsFile.IsDevelopmentName(e));
        if (clearsBase && hasOtherEnvironments)
        {
            notices.Add(new PlanNotice(NoticeSeverity.Warning,
                "appsettings.json ve ek yapılandırma dosyaları tüm ortamlarda yüklenir. Buradan temizlenen değerler Development dışı ortamlarda " +
                "ortam değişkeni veya secret vault ile sağlanmalıdır."));
        }
    }
}
