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
    private bool _suppressAutoSelect;

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

        // При старте пресет не выбран: иначе любое открытое видео сразу получало бы
        // настройки первого пресета в списке — а это квадратный стикер на три секунды.
        _suppressAutoSelect = true;
        SelectedGroup = Groups.FirstOrDefault();
        _suppressAutoSelect = false;

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
    [NotifyPropertyChangedFor(nameof(HasDurationLimit))]
    private PresetDefinition? _selectedPreset;

    [ObservableProperty]
    private DurationFitOption _durationFit = DurationFitOption.All[0];

    [ObservableProperty]
    private PresetDefinition? _appliedPreset;

    public bool HasSelection => SelectedPreset is not null;

    /// <summary>
    /// Есть ли у пресета предел длительности. Выбор «обрезать или ускорить» имеет смысл
    /// только там, где ролик может в этот предел не влезть: у видеостикеров Telegram он
    /// есть, у общих пресетов — нет. Смотрим именно на лимит из JSON, а не на название
    /// площадки: добавленный пользователем пресет со своим пределом получит этот выбор
    /// сам, без правки интерфейса.
    /// </summary>
    public bool HasDurationLimit => SelectedPreset?.Constraints.MaxDuration is not null;

    public bool HasViolations => Violations.Count > 0;

    public void Attach(Project project)
    {
        _project = project;
        Changes.Clear();
        Violations.Clear();
        AppliedPreset = null;

        // Пресет могли выбрать до открытия файла — тогда его параметры применяются сейчас.
        ApplyTemplateOnly(SelectedPreset);
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

        SelectedPreset = _suppressAutoSelect ? null : Presets.FirstOrDefault();
    }

    partial void OnSelectedPresetChanged(PresetDefinition? value)
    {
        Notes.Clear();

        foreach (var note in value?.Notes ?? [])
        {
            Notes.Add(note);
        }

        ApplyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelection));

        // Формат, кодек и размер встают сразу при выборе: видеть «MP4» под надписью
        // «Видеостикер» бессмысленно. Монтаж при этом не трогаем — за него отвечает
        // кнопка, где пользователь выбирает, обрезать ролик или ускорить.
        ApplyTemplateOnly(value);
    }

    private void ApplyTemplateOnly(PresetDefinition? preset)
    {
        if (_project is null || preset is null)
        {
            AppliedPreset = null;
            Changes.Clear();
            Violations.Clear();
            OnPropertyChanged(nameof(HasViolations));
            return;
        }

        var result = _applier.Apply(preset, _project, _export.BuildSettings(), DurationFitMode.Keep);

        _export.ApplySettings(result.Settings);

        Changes.Clear();
        foreach (var change in result.Changes)
        {
            Changes.Add(change.Description);
        }

        AppliedPreset = preset;
        ShowViolations(result.Validation);
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
