using System.Text.Encodings.Web;
using System.Text.Json;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Core.Projects;

/// <summary>
/// Черновик в JSON рядом с исходниками или где укажет пользователь.
/// </summary>
/// <remarks>
/// Читаемый текст, а не свой двоичный формат: черновик — это разметка монтажа,
/// и человеку должно быть видно, что именно в нём записано. Пути к файлам внутри
/// абсолютные: переносить проект вместе с исходниками пока не обещаем.
/// </remarks>
public sealed class JsonProjectStore(IMediaProbe probe, ILogger<JsonProjectStore> logger) : IProjectStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task SaveAsync(Project project, string path, CancellationToken cancellationToken)
    {
        var document = ProjectDocumentWriter.Create(project);

        // Как и при экспорте: сначала во временный файл, потом подменяем.
        // Иначе сбой на середине записи оставил бы обрезанный черновик вместо целого.
        var temporary = path + ".part";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);

        logger.LogInformation("Черновик сохранён: {Path} ({Clips} клипов)", path, document.Clips.Count);
    }

    public async Task<ProjectLoadResult> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ProjectDocument? document;

        await using (var stream = File.OpenRead(path))
        {
            document = await JsonSerializer
                .DeserializeAsync<ProjectDocument>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
        }

        if (document is null)
        {
            throw new EditOperationException("Файл черновика пуст или испорчен.");
        }

        if (document.SchemaVersion > 1)
        {
            throw new EditOperationException(
                $"Черновик сделан более новой версией Meows Cut (формат {document.SchemaVersion}).");
        }

        var media = new Dictionary<Guid, MediaInfo>();
        var warnings = new List<string>();

        foreach (var source in document.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(source.Path))
            {
                warnings.Add($"Файл не найден: {source.Path}");
                continue;
            }

            try
            {
                media[source.Id] = await probe.ProbeAsync(source.Path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Не удалось прочитать {Path} из черновика", source.Path);
                warnings.Add($"Файл не читается: {Path.GetFileName(source.Path)}");
            }
        }

        var (project, restoreWarnings) = ProjectDocumentReader.Restore(document, media);

        warnings.AddRange(restoreWarnings);
        logger.LogInformation("Черновик открыт: {Path}, клипов {Clips}", path, project.Sequence.Video.Clips.Count);

        return new ProjectLoadResult(project, warnings);
    }
}
