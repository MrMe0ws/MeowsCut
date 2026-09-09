namespace MeowsCut.Core.Jobs;

public enum JobStatus
{
    Queued = 0,
    Running,
    Completed,
    Failed,
    Canceled
}

public enum JobKind
{
    Export = 0,
    Preview,
    Thumbnails
}

/// <summary>
/// Состояние выполнения задачи. Единственное, что видит интерфейс во время работы.
/// </summary>
public sealed record JobProgress(
    double Percent,
    TimeSpan Elapsed,
    TimeSpan? Remaining,
    string StageName,
    string? Detail,
    int StageIndex,
    int StageCount)
{
    public static readonly JobProgress Empty =
        new(0, TimeSpan.Zero, null, string.Empty, null, 0, 1);

    public bool HasEstimate => Remaining.HasValue;
}

public sealed record JobResult(JobStatus Status, string? OutputPath, Exception? Error)
{
    public static JobResult Success(string outputPath) => new(JobStatus.Completed, outputPath, null);

    public static JobResult Canceled() => new(JobStatus.Canceled, null, null);

    public static JobResult Failed(Exception error) => new(JobStatus.Failed, null, error);

    public bool IsSuccess => Status == JobStatus.Completed;
}

/// <summary>Описание задачи для очереди: заголовок и сама работа.</summary>
public sealed record JobDescriptor(
    string Title,
    JobKind Kind,
    Func<IProgress<JobProgress>, CancellationToken, Task<JobResult>> Work);
