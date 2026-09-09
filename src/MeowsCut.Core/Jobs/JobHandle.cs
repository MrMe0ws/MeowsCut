namespace MeowsCut.Core.Jobs;

/// <summary>
/// Ручка запущенной задачи: состояние, прогресс, отмена и ожидание результата.
/// </summary>
public sealed class JobHandle : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly TaskCompletionSource<JobResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _disposed;

    internal JobHandle(JobDescriptor descriptor, CancellationToken applicationShutdown)
    {
        Descriptor = descriptor;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(applicationShutdown);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public JobDescriptor Descriptor { get; }

    public string Title => Descriptor.Title;

    public JobStatus Status { get; private set; } = JobStatus.Queued;

    public JobProgress Progress { get; private set; } = JobProgress.Empty;

    public Task<JobResult> Completion => _completion.Task;

    internal CancellationToken Token => _cancellation.Token;

    public bool IsCancellationRequested => _cancellation.IsCancellationRequested;

    public event EventHandler<JobProgress>? ProgressChanged;

    public event EventHandler<JobStatus>? StatusChanged;

    /// <summary>Отмена идемпотентна: повторный клик по кнопке ничего не ломает.</summary>
    public void Cancel()
    {
        if (_disposed || _cancellation.IsCancellationRequested)
        {
            return;
        }

        _cancellation.Cancel();
    }

    internal void SetStatus(JobStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    internal void ReportProgress(JobProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(this, progress);
    }

    internal void Complete(JobResult result)
    {
        SetStatus(result.Status);
        _completion.TrySetResult(result);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Dispose();
    }
}
