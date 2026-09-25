using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UserSecretManager.Core.Analysis;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Effective;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>What the application would see for a chosen environment and launch profile.</summary>
public sealed partial class EffectiveTabViewModel : ObservableObject
{
    private static readonly string[] DefaultEnvironments = [AppSettingsFile.DevelopmentEnvironment, "Staging", "Production"];

    private ProjectConfiguration? _configuration;
    private List<EffectiveRowViewModel> _allRows = [];
    private bool _loading;

    [ObservableProperty]
    private string _environment = AppSettingsFile.DevelopmentEnvironment;

    [ObservableProperty]
    private LaunchProfileOption? _selectedProfile;

    [ObservableProperty]
    private bool _includeUserSecrets = true;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private bool _showEmptyOnly;

    [ObservableProperty]
    private bool _revealValues;

    public ObservableCollection<string> Environments { get; } = [];

    public ObservableCollection<LaunchProfileOption> Profiles { get; } = [];

    public ObservableCollection<EffectiveRowViewModel> Rows { get; } = [];

    public int EmptyCount => _allRows.Count(r => r.IsEmpty);

    public bool HasEmpty => EmptyCount > 0;

    public bool IsEmpty => Rows.Count == 0;

    public bool IsNonDevelopment => !AppSettingsFile.IsDevelopmentName(Environment);

    public string Summary => $"{_allRows.Count} anahtar · {_allRows.Count(r => r.IsOverridden)} ezilmiş · {EmptyCount} boş";

    public string EmptyWarning => IsNonDevelopment
        ? $"{Environment} ortamında {EmptyCount} anahtarın değeri boş. User secrets bu ortamda yüklenmediği için bu değerler ortam değişkeni veya bir secret vault ile sağlanmalı."
        : $"{Environment} ortamında {EmptyCount} anahtarın değeri boş.";

    public void Load(ProjectConfiguration configuration)
    {
        _loading = true;
        _configuration = configuration;
        var previousEnvironment = Environment;
        var previousProfile = SelectedProfile?.Profile?.Name;

        Environments.Clear();
        foreach (var name in DefaultEnvironments
                     .Concat(configuration.Files.Select(f => f.Environment).OfType<string>())
                     .Concat(configuration.Project.LaunchEnvironments)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Environments.Add(name);
        }

        Profiles.Clear();
        Profiles.Add(new LaunchProfileOption(null));
        foreach (var profile in configuration.Project.LaunchProfiles)
        {
            Profiles.Add(new LaunchProfileOption(profile));
        }

        Environment = Environments.FirstOrDefault(e => string.Equals(e, previousEnvironment, StringComparison.OrdinalIgnoreCase))
                      ?? AppSettingsFile.DevelopmentEnvironment;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Profile?.Name == previousProfile) ?? Profiles[0];
        _loading = false;
        Rebuild();
    }

    partial void OnEnvironmentChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        IncludeUserSecrets = AppSettingsFile.IsDevelopmentName(value);
        Rebuild();
    }

    partial void OnSelectedProfileChanged(LaunchProfileOption? value)
    {
        if (_loading || value?.Profile?.Environment is not { } environment)
        {
            Rebuild();
            return;
        }

        Environment = Environments.FirstOrDefault(e => string.Equals(e, environment, StringComparison.OrdinalIgnoreCase)) ?? environment;
        Rebuild();
    }

    partial void OnIncludeUserSecretsChanged(bool value) => Rebuild();

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnShowEmptyOnlyChanged(bool value) => ApplyFilter();

    partial void OnRevealValuesChanged(bool value)
    {
        foreach (var row in _allRows)
        {
            row.Reveal = value;
        }
    }

    private void Rebuild()
    {
        if (_loading || _configuration is null || string.IsNullOrWhiteSpace(Environment))
        {
            return;
        }

        var options = new EffectiveConfigurationOptions(Environment.Trim(), IncludeUserSecrets, SelectedProfile?.Profile);
        _allRows = EffectiveConfigurationBuilder.Build(_configuration, options)
            .Select(e => new EffectiveRowViewModel(e) { Reveal = RevealValues })
            .ToList();
        ApplyFilter();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmptyCount));
        OnPropertyChanged(nameof(HasEmpty));
        OnPropertyChanged(nameof(EmptyWarning));
        OnPropertyChanged(nameof(IsNonDevelopment));
    }

    private void ApplyFilter()
    {
        var terms = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Rows.Clear();
        foreach (var row in _allRows.Where(r => (!ShowEmptyOnly || r.IsEmpty) &&
                                                terms.All(t => r.Key.Contains(t, StringComparison.OrdinalIgnoreCase))))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed record LaunchProfileOption(LaunchProfile? Profile)
{
    public string Label => Profile is null
        ? "(launch profili yok)"
        : Profile.Environment is { } environment ? $"{Profile.Name} — {environment}" : Profile.Name;
}

public sealed partial class EffectiveRowViewModel(EffectiveEntry entry) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    private bool _reveal;

    public string Key => entry.Key;

    public bool IsEmpty => entry.IsEmpty;

    public bool IsOverridden => entry.IsOverridden;

    public bool IsSensitive => entry.Hint.Level != SensitivityLevel.None || entry.Winner.Kind == ConfigLayerKind.UserSecrets;

    public string DisplayValue => entry.IsEmpty ? "\"\""
        : IsSensitive && !Reveal ? ValueMask.Mask(entry.Value)
        : entry.Value!;

    public string Source => entry.Winner.Source;

    public bool IsFromSecrets => entry.Winner.Kind == ConfigLayerKind.UserSecrets;

    public bool IsFromLaunchProfile => entry.Winner.Kind == ConfigLayerKind.LaunchProfile;

    public bool IsFromCustomFile => entry.Winner.Kind == ConfigLayerKind.CustomFile;

    public bool IsFromFile => entry.Winner.Kind is ConfigLayerKind.Base or ConfigLayerKind.EnvironmentFile;

    public string OverrideText => entry.IsOverridden ? $"{entry.Layers.Count - 1} katmanı ezer" : string.Empty;

    public string LayersToolTip => string.Join(System.Environment.NewLine, entry.Layers.Select((l, i) =>
        $"{i + 1}. {l.Source}: {(string.IsNullOrEmpty(l.Value) ? "\"\"" : IsSensitive ? ValueMask.Mask(l.Value) : l.Value)}"
        + (i == entry.Layers.Count - 1 ? "   ← kullanılan" : string.Empty)));
}
