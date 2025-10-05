using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

internal class DbQueueService<TContext>(IOptions<DbQueueServiceOptions> options, IServiceProvider sp) : BackgroundService where TContext : DbContext
{
    private Channel<QueueItem> channel = Channel.CreateUnbounded<QueueItem>(new() { SingleReader = true });

    public async Task ExecuteAsync(Func<TContext, CancellationToken, Task> action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        TaskCompletionSource tcs = new();
        await channel.Writer.WriteAsync(new(tcs, action, ct), ct);
        await tcs.Task;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<Task> tasks = new();

        var reader = channel.Reader;
        while (await reader.WaitToReadAsync(stoppingToken))
        {
            List<QueueItem> batch = new();

            if (tasks.Count >= options.Value.MaxConcurrentBatches) await Task.WhenAny(tasks.ToArray());

            while (batch.Count < options.Value.MaxItemsInBatch
                    && reader.TryRead(out var item)) batch.Add(item);

            tasks.RemoveAll(t => t.IsCompleted);
            tasks.Add(ProcessBatch(sp, batch, stoppingToken));
        }
        await Task.WhenAny(tasks.ToArray());
    }

    private async Task ProcessBatch(IServiceProvider sp, IEnumerable<QueueItem> batch, CancellationToken stoppingToken)
    {
        await using var scope = sp.CreateAsyncScope();
        await using var ctx = scope.ServiceProvider.GetRequiredService<TContext>();
        foreach (var item in batch)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                item.Source.SetCanceled(stoppingToken);
                continue;
            }

            var result = item.Action(ctx, item.CancellationToken);
            await result.WaitAsync(item.CancellationToken)
                        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            item.Source.SetFromTask(result);
        }
    }

    private readonly record struct QueueItem(TaskCompletionSource Source, Func<TContext, CancellationToken, Task> Action, CancellationToken CancellationToken);
}

internal class DbQueueServiceOptions
{
    public int MaxItemsInBatch { get; set; } = 50;
    public int MaxConcurrentBatches { get; set; } = 15;
} 