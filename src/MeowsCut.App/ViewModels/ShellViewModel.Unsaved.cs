using CommunityToolkit.Mvvm.ComponentModel;
using MeowsCut.App.Localization;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Несохранённые правки: видно, есть ли они, и они не пропадают молча.
/// </summary>
/// <remarks>
/// Раньше «Закрыть проект», выход из программы и открытие другого черновика
/// просто выбрасывали монтаж, а «Сохранить» не давало никакого видимого ответа.
/// Правки терялись без единого слова: восстановленный монтаж заменяли открытым
/// черновиком — и автосохранение тут же перезаписывалось старой версией.
/// </remarks>
public sealed partial class ShellViewModel
{
    /// <summary>Есть правки, которых нет в файле черновика.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStateText))]
    private bool _hasUnsavedChanges;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStateText))]
    private DateTime? _savedAt;

    /// <summary>Строка рядом с именем черновика: «не сохранено» или «сохранено в 15:20».</summary>
    public string? SaveStateText =>
        Project is null ? null
        : HasUnsavedChanges ? Strings.ProjectStateUnsaved
        : SavedAt is { } time ? string.Format(Strings.ProjectStateSaved, time)
        : null;

    /// <summary>
    /// Можно ли выбросить текущий монтаж. Если в нём есть несохранённое — спрашиваем:
    /// сохранить, не сохранять или передумать.
    /// </summary>
    /// <returns>false — пользователь передумал или сохранить не удалось.</returns>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (Project is null || !HasUnsavedChanges)
        {
            return true;
        }

        var message = ProjectFileName is { } name
            ? string.Format(Strings.SaveChangesMessage, name)
            : Strings.SaveChangesMessageNew;

        var answer = _dialogService.Ask(
            Strings.SaveChangesTitle,
            message,
            Strings.SaveChangesAccept,
            Strings.SaveChangesDecline,
            Strings.SaveChangesCancel);

        switch (answer)
        {
            case true:
                await SaveProjectAsync(CancellationToken.None).ConfigureAwait(true);

                // Отказались выбирать файл или запись не удалась — работу не бросаем.
                return !HasUnsavedChanges;

            case false:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Окно закрывается после ответа на вопрос о сохранении. Страховка больше
    /// не нужна: монтаж либо в файле, либо его осознанно выбросили.
    /// </summary>
    public void ForgetRecovery() => _autosave.Clear();

    private void MarkSaved()
    {
        HasUnsavedChanges = false;
        SavedAt = DateTime.Now;
    }

    /// <summary>Только что открытое ещё не правили: и спрашивать о нём нечего.</summary>
    private void MarkFresh()
    {
        HasUnsavedChanges = false;
        SavedAt = null;
        OnPropertyChanged(nameof(SaveStateText));
    }
}
