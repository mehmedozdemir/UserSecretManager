using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>Review and adjust a migration plan before applying it.</summary>
public sealed partial class MigrationReviewViewModel : DialogViewModelBase
{
    private readonly MigrationPlan _plan;

    [ObservableProperty]
    private bool _revealValues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanTab), nameof(IsPreviewTab))]
    private int _selectedTab;

    [ObservableProperty]
    private string? _error;

    public MigrationReviewViewModel(MigrationPlan plan)
    {
        _plan = plan;
        Items = plan.Items.Select(i => new MigrationItemViewModel(i, this, OnPlanChanged)).ToList();
        Notices = plan.Notices.Select(n => new NoticeViewModel(n.Severity, n.Message)).ToList();
        RefreshPreview();
    }

    public override string Title => "Secret'a taşıma — inceleme";

    public override double DialogWidth => 1040;

    public override double DialogHeight => 720;

    public override bool CanResize => true;

    public IReadOnlyList<MigrationItemViewModel> Items { get; }

    public IReadOnlyList<NoticeViewModel> Notices { get; }

    public bool HasNotices => Notices.Count > 0;

    public ChangePreviewViewModel Preview { get; } = new();

    public ChangeSet? ChangeSet { get; private set; }

    public bool IsPlanTab => SelectedTab == 0;

    public bool IsPreviewTab => SelectedTab == 1;

    public string Summary
    {
        get
        {
            var clears = Items.Sum(i => i.FileActions.Count(a => a.IsEnabled));
            var conflicts = Items.Count(i => i.HasConflict);
            var text = $"{Items.Count} secret yazılacak · {clears} değer appsettings'ten temizlenecek";
            return conflicts > 0 ? $"{text} · {conflicts} çakışma" : text;
        }
    }

    public bool CanApply => !_plan.IsBlocked && ChangeSet is { IsEmpty: false } && Error is null;

    [RelayCommand]
    private void ShowPlan() => SelectedTab = 0;

    [RelayCommand]
    private void ShowPreview() => SelectedTab = 1;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => Close(true);

    partial void OnRevealValuesChanged(bool value)
    {
        foreach (var item in Items)
        {
            item.RefreshMask();
        }
    }

    private void OnPlanChanged()
    {
        RefreshPreview();
        OnPropertyChanged(nameof(Summary));
    }

    private void RefreshPreview()
    {
        try
        {
            ChangeSet = _plan.IsBlocked ? null : ChangeSetFactory.FromPlan(_plan);
            Error = null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ChangeSet = null;
            Error = ex.Message;
        }

        Preview.Load(ChangeSet);
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class MigrationItemViewModel
{
    public MigrationItemViewModel(MigrationItem item, MigrationReviewViewModel owner, Action changed)
    {
        Key = item.Key;
        HasConflict = item.HasConflict;
        EnvironmentVariableName = item.EnvironmentVariableName;
        Candidates = item.Candidates
            .Select(c => new CandidateViewModel(c, item, owner, changed))
            .ToList();
        FileActions = item.FileActions.Select(a => new FileActionViewModel(a, changed)).ToList();
    }

    public string Key { get; }

    public bool HasConflict { get; }

    public string EnvironmentVariableName { get; }

    public IReadOnlyList<CandidateViewModel> Candidates { get; }

    public IReadOnlyList<FileActionViewModel> FileActions { get; }

    public void RefreshMask()
    {
        foreach (var candidate in Candidates)
        {
            candidate.RefreshMask();
        }
    }
}

public sealed partial class CandidateViewModel(
    ValueCandidate candidate, MigrationItem item, MigrationReviewViewModel owner, Action changed) : ObservableObject
{
    public string DisplayValue => owner.RevealValues ? candidate.Value : ValueMask.Mask(candidate.Value);

    public string Sources { get; } = string.Join(", ", candidate.Sources);

    public bool IsExistingSecret { get; } = candidate.IsExistingSecret;

    public string GroupName { get; } = "c_" + item.Key;

    public bool IsSelected
    {
        get => ReferenceEquals(item.Selected, candidate);
        set
        {
            if (!value || ReferenceEquals(item.Selected, candidate))
            {
                return;
            }

            item.Selected = candidate;
            OnPropertyChanged();
            changed();
        }
    }

    public void RefreshMask() => OnPropertyChanged(nameof(DisplayValue));
}

public sealed partial class FileActionViewModel(FileClearAction action, Action changed) : ObservableObject
{
    public string FileName { get; } = action.File.FileName;

    public string EnvironmentLabel { get; } = action.File.DisplayName;

    public string? Warning { get; } = action.Warning;

    public bool HasWarning => Warning is not null;

    public bool IsEnabled
    {
        get => action.IsEnabled;
        set
        {
            if (action.IsEnabled == value)
            {
                return;
            }

            action.IsEnabled = value;
            OnPropertyChanged();
            changed();
        }
    }
}

public sealed class NoticeViewModel(NoticeSeverity severity, string message)
{
    public string Message { get; } = message;

    public bool IsError { get; } = severity == NoticeSeverity.Error;

    public bool IsWarning { get; } = severity == NoticeSeverity.Warning;

    public bool IsInfo { get; } = severity == NoticeSeverity.Info;
}

public static class ValueMask
{
    private const int VisiblePrefix = 3;
    private const int MaskLength = 10;

    /// <summary>Shows only enough of a value to tell values apart.</summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= VisiblePrefix * 2
            ? new string('•', MaskLength)
            : string.Concat(value.AsSpan(0, VisiblePrefix), new string('•', MaskLength));
    }
}
