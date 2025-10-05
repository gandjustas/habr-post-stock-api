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
        var reader = channel.Reader;

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            List<QueueItem> batch = new();
            while (batch.Count < options.Value.MaxItemsInBatch
                    && reader.TryRead(out var item)) batch.Add(item);

            await ProcessBatch(sp, batch, stoppingToken);
        }

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
} 