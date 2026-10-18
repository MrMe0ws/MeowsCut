using System.IO;
using MeowsCut.App.Localization;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Озвучка с микрофона: оболочка добавляет записанный файл в проект,
/// на доску его кладёт уже сама доска.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>Запись голоса поверх ролика.</summary>
    public VoiceoverViewModel Voiceover { get; private set; } = null!;

    private void AttachVoiceover(VoiceoverViewModel voiceover)
    {
        Voiceover = voiceover;
        voiceover.ImportRecording = ImportRecordingAsync;
        voiceover.ProjectFolder = () => ProjectPath is { } path ? Path.GetDirectoryName(path) : null;
        voiceover.CanStartNow = () => CanStartWork;
    }

    /// <summary>
    /// Читает записанный файл и добавляет его в проект как источник.
    /// </summary>
    /// <remarks>
    /// Через ffprobe, как любой другой файл: длительность и формат записи должны
    /// браться оттуда же, откуда их возьмёт экспорт, иначе кусок на доске и звук
    /// в готовом ролике разошлись бы на долю секунды.
    /// </remarks>
    private async Task<(Project Project, MediaSource Source)?> ImportRecordingAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (Project is null)
        {
            return null;
        }

        BusyText = Strings.BusyAddingRecording;
        IsBusy = true;
        try
        {
            var info = await _mediaProbe.ProbeAsync(path, cancellationToken).ConfigureAwait(true);
            var (project, source) = Project.WithSource(info);

            Project = project;
            Preview.UpdateProject(project);
            Sources.Update(project);

            return (project, source);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (MeowsCutException ex)
        {
            _logger.LogWarning(ex, "Не удалось добавить запись {Path}", path);
            _dialogService.ShowError(_errorPresenter.Present(ex));
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
