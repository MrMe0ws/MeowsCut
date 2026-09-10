using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Presets;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>Пункт списка «что делать, если ролик длиннее лимита».</summary>
public sealed record DurationFitOption(DurationFitMode Mode, string Title)
{
    public static readonly IReadOnlyList<DurationFitOption> All =
    [
        new(DurationFitMode.Trim, "Обрезать лишнее"),
        new(DurationFitMode.SpeedUp, "Ускорить целиком")
    ];

    public override string ToString() => Title;
}

/// <summary>
/// Пресеты площадок, включая Telegram.
/// </summary>
/// <remarks>
/// Пресет — стартовая точка, а не клетка: после применения все поля остаются
/// доступными, а проверка ограничений продолжает работать и подсказывать,
/// если настройки развели с требованиями площадки.
/// </remarks>
public sealed partial class PresetsViewModel : ObservableObject
{
    private readonly IPresetProvider _provider;
    private readonly IPresetApplier _applier;
    private readonly IPresetValidator _validator;
    private readonly ExportViewModel _export;
    private readonly TimelineViewModel _timeline;
    private readonly ILogger<PresetsViewModel> _logger;

    private Project? _project;

    public PresetsViewModel(
        IPresetProvider provider,
        IPresetApplier applier,
        IPresetValidator validator,
        ExportViewModel export,
        TimelineViewModel timeline,
        ILogger<PresetsViewModel> logger)
    {
        _provider = provider;
        _applier = applier;
        _validator = validator;
        _export = export;
        _timeline = timeline;
        _logger = logger;

        foreach (var group in provider.Groups)
        {
            Groups.Add(group);
        }

        SelectedGroup = Groups.FirstOrDefault();

        // Настройки могли поменять руками — требования площадки должны продолжать
        // проверяться, а не молчать до самого экспорта.
        _export.PropertyChanged += (_, _) => RefreshValidation();
        _timeline.SequenceChanged += (_, _) => RefreshValidation();
    }

    public ObservableCollection<PresetGroup> Groups { get; } = [];

    public ObservableCollection<PresetDefinition> Presets { get; } = [];

    public ObservableCollection<string> Changes { get; } = [];

    public ObservableCollection<string> Notes { get; } = [];

    public ObservableCollection<ConstraintViolation> Violations { get; } = [];

    public IReadOnlyList<DurationFitOption> DurationFits { get; } = DurationFitOption.All;

    [ObservableProperty]
    private PresetGroup? _selectedGroup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PresetDefinition? _selectedPreset;

    [ObservableProperty]
    private DurationFitOption _durationFit = DurationFitOption.All[0];

    [ObservableProperty]
    private PresetDefinition? _appliedPreset;

    public bool HasSelection => SelectedPreset is not null;

    public bool HasViolations => Violations.Count > 0;

    public void Attach(Project project)
    {
        _project = project;
        Changes.Clear();
        Violations.Clear();
        AppliedPreset = null;
        RefreshValidation();
    }

    public void Detach()
    {
        _project = null;
        Changes.Clear();
        Violations.Clear();
        AppliedPreset = null;
    }

    /// <summary>Проект изменился на доске — пересчитываем нарушения.</summary>
    public void UpdateProject(Project project)
    {
        _project = project;
        RefreshValidation();
    }

    partial void OnSelectedGroupChanged(PresetGroup? value)
    {
        Presets.Clear();
        Notes.Clear();

        if (value is null)
        {
            return;
        }

        foreach (var preset in _provider.GetPresets(value.Id))
        {
            Presets.Add(preset);
        }

        SelectedPreset = Presets.FirstOrDefault();
    }

    partial void OnSelectedPresetChanged(PresetDefinition? value)
    {
        Notes.Clear();

        foreach (var note in value?.Notes ?? [])
        {
            Notes.Add(note);
        }

        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (_project is null || SelectedPreset is not { } preset)
        {
            return;
        }

        var result = _applier.Apply(preset, _project, _export.BuildSettings(), DurationFit.Mode);

        // Монтаж пресет тоже меняет — это должно отменяться Ctrl+Z наравне с ножницами.
        if (!ReferenceEquals(result.Project.Sequence, _project.Sequence))
        {
            _timeline.ApplySequence(result.Project.Sequence, $"Пресет: {preset.Title}");
        }

        _project = result.Project;
        _export.ApplySettings(result.Settings);

        Changes.Clear();
        foreach (var change in result.Changes)
        {
            Changes.Add(change.Description);
        }

        AppliedPreset = preset;
        ShowViolations(result.Validation);

        _logger.LogInformation("Применён пресет {Preset}, изменений: {Count}", preset.Id, result.Changes.Count);
    }

    private bool CanApply() => _project is not null && SelectedPreset is not null;

    /// <summary>Повторное применение пресета и есть исправление: шаблон возвращает всё к требованиям.</summary>
    [RelayCommand]
    private void FixViolations() => Apply();

    private void RefreshValidation()
    {
        if (_project is null || AppliedPreset is not { } preset)
        {
            return;
        }

        ShowViolations(_validator.Validate(preset.Constraints, _project, _export.BuildSettings()));
    }

    private void ShowViolations(PresetValidationResult validation)
    {
        Violations.Clear();

        foreach (var violation in validation.Violations)
        {
            Violations.Add(violation);
        }

        OnPropertyChanged(nameof(HasViolations));
    }
}
