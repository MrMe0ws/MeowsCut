namespace MeowsCut.Core.Editing;

/// <summary>
/// Идентификатор источника (файла) внутри проекта.
/// </summary>
public readonly record struct SourceId(Guid Value)
{
    public static SourceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Идентификатор клипа на таймлайне. Нужен, чтобы выделение и перетаскивание
/// переживали пересборку списка после каждой команды редактирования.
/// </summary>
public readonly record struct ClipId(Guid Value)
{
    public static ClipId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}
