using System.Text.Json.Serialization;

namespace MeowsCut.Core.Projects;

/// <summary>
/// Что известно про черновик, сохранённый редактором самостоятельно.
/// </summary>
/// <remarks>
/// Лежит рядом с самим черновиком отдельным файлом. В сам `.meows` эти сведения
/// не кладутся: это формат пользовательского документа, и служебная пометка
/// «откуда восстанавливать» в нём была бы лишней для всех, кроме нас.
/// </remarks>
public sealed class AutosaveState
{
    /// <summary>Путь к черновику пользователя, если монтаж уже сохраняли руками.</summary>
    [JsonPropertyName("projectPath")]
    public string? ProjectPath { get; init; }

    /// <summary>Имя первого файла на доске — по нему пользователь узнаёт свой монтаж.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("savedAt")]
    public DateTimeOffset SavedAt { get; init; }
}
