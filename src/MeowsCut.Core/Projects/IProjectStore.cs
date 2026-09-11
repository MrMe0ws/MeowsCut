using MeowsCut.Core.Editing;

namespace MeowsCut.Core.Projects;

/// <summary>Черновик проекта: сохранить, чтобы вернуться к монтажу позже.</summary>
public interface IProjectStore
{
    /// <summary>Расширение файла черновика.</summary>
    const string Extension = ".meows";

    Task SaveAsync(Project project, string path, CancellationToken cancellationToken);

    /// <summary>
    /// Читает черновик. Файлы перечитываются заново, поэтому результат может
    /// отличаться от сохранённого: пропавшие файлы и не влезшие куски перечислены
    /// в предупреждениях.
    /// </summary>
    Task<ProjectLoadResult> LoadAsync(string path, CancellationToken cancellationToken);
}

/// <summary>Что удалось прочитать из черновика.</summary>
public sealed record ProjectLoadResult(Project Project, IReadOnlyList<string> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
}
