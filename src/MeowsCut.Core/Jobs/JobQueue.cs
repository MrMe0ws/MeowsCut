using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Core.Jobs;

/// <summary>
/// Очередь длительных операций.
/// </summary>
public interface IJobQueue : IAsyncDisposable
{
    IReadOnlyList<JobHandle> ActiveJobs { get; }

    JobHandle Enqueue(JobDescriptor descriptor);

    event EventHandler<JobHandle>? JobStarted;

    event EventHandler<JobHandle>? JobFinished;

    /// <summary>Отменяет всё и дожидается завершения — вызывается при выходе из приложения.</summary>
    Task ShutdownAsync();
}

/// <summary>
/// Очередь на канале с одним рабочим потоком.
/// </summary>
/// <remarks>
/// Даже когда задача одна, очередь нужна: это единое место для прогресса, отмены,
/// логирования и корректного завершения приложения. Параллелизм равен одному
/// осознанно — кодирование и так занимает все ядра, и параллельный запуск
/// только замедлил бы обе задачи.
/// </remarks>
public sealed class JobQueue : IJobQueue
{
    private readonly Channel<JobHandle> _channel = Channel.CreateUnbounded<JobHandle>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<Guid, JobHandle> _active = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ILogger<JobQueue> _logger;
    private readonly Task _worker;

    public JobQueue(ILogger<JobQueue> logger)
    {
        _logger = logger;
        _worker = Task.Run(ProcessAsync);
    }

    public IReadOnlyList<JobHandle> ActiveJobs => _active.Values.ToArray();

    public event EventHandler<JobHandle>? JobStarted;

    public event EventHandler<JobHandle>? JobFinished;

    public JobHandle Enqueue(JobDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var handle = new JobHandle(descriptor, _shutdown.Token);
        _active[handle.Id] = handle;

        if (!_channel.Writer.TryWrite(handle))
        {
            handle.Complete(JobResult.Canceled());
            _active.TryRemove(handle.Id, out _);
        }

        _logger.LogInformation("Задача поставлена в очередь: {Title}", descriptor.Title);
        return handle;
    }

    private async Task ProcessAsync()
    {
        await foreach (var handle in _channel.Reader.ReadAllAsync())
        {
            await RunAsync(handle).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(JobHandle handle)
    {
        using var scope = _logger.BeginScope("JobId:{JobId}", handle.Id);

        if (handle.IsCancellationRequested)
        {
            Finish(handle, JobResult.Canceled());
            return;
        }

        handle.SetStatus(JobStatus.Running);
        JobStarted?.Invoke(this, handle);
        _logger.LogInformation("Задача запущена: {Title}", handle.Title);

        var progress = new Progress<JobProgress>(handle.ReportProgress);

        try
        {
            var result = await handle.Descriptor.Work(progress, handle.Token).ConfigureAwait(false);
            _logger.LogInformation("Задача завершена: {Title}, статус {Status}", handle.Title, result.Status);
            Finish(handle, result);
        }
        catch (OperationCanceledException)
        {
            // Отмена — не ошибка: пользователь так решил.
            _logger.LogInformation("Задача отменена: {Title}", handle.Title);
            Finish(handle, JobResult.Canceled());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Задача завершилась ошибкой: {Title}", handle.Title);
            Finish(handle, JobResult.Failed(ex));
        }
    }

    private void Finish(JobHandle handle, JobResult result)
    {
        _active.TryRemove(handle.Id, out _);
        handle.Complete(result);
        JobFinished?.Invoke(this, handle);
    }

    public async Task ShutdownAsync()
    {
        foreach (var handle in ActiveJobs)
        {
            handle.Cancel();
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        _channel.Writer.TryComplete();

        try
        {
            await _worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Рабочий поток очереди не завершился за отведённое время");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
