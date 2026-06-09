using Confluent.Kafka;
using EShop.Messaging.Kafka.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Messaging.Kafka;

/// <summary>
/// Polls the configured DbContext for un-dispatched OutboxMessage rows and produces them to Kafka.
/// Pending rows are produced in OccurredAt order; rows are marked DispatchedAt on success.
/// </summary>
public sealed class OutboxDispatcherBackgroundService<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KafkaOptions> _options;
    private readonly ILogger<OutboxDispatcherBackgroundService<TDbContext>> _logger;

    public OutboxDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<OutboxDispatcherBackgroundService<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var pollDelay = TimeSpan.FromSeconds(Math.Max(1, opts.OutboxPollingIntervalSeconds));

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            AllowAutoCreateTopics = true,
        };

        using var producer = new ProducerBuilder<string, string>(producerConfig)
            .SetLogHandler((_, msg) =>
            {
                if (msg.Level <= SyslogLevel.Warning)
                {
                    _logger.LogWarning("Kafka producer log [{Level}] {Facility}: {Message}", msg.Level, msg.Facility, msg.Message);
                }
            })
            .Build();

        _logger.LogInformation(
            "Outbox dispatcher started for {DbContext} (interval={Interval}s, bootstrap={Bootstrap})",
            typeof(TDbContext).Name, pollDelay.TotalSeconds, opts.BootstrapServers);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(producer, opts.OutboxBatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher iteration failed");
            }

            try
            {
                await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        producer.Flush(TimeSpan.FromSeconds(5));
    }

    private async Task DispatchBatchAsync(IProducer<string, string> producer, int batchSize, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var pending = await db.Set<OutboxMessage>()
            .Where(x => x.DispatchedAt == null)
            .OrderBy(x => x.OccurredAt)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var msg in pending)
        {
            try
            {
                var result = await producer.ProduceAsync(
                    msg.Topic,
                    new Message<string, string>
                    {
                        Key = msg.PartitionKey ?? msg.Id.ToString(),
                        Value = msg.Payload,
                    },
                    ct).ConfigureAwait(false);

                msg.DispatchedAt = DateTimeOffset.UtcNow;
                msg.Attempts += 1;
                msg.LastError = null;

                _logger.LogInformation(
                    "Outbox -> Kafka topic {Topic} partition {Partition} offset {Offset} (eventId {Id})",
                    result.Topic, result.Partition.Value, result.Offset.Value, msg.Id);
            }
            catch (Exception ex)
            {
                msg.Attempts += 1;
                msg.LastError = ex.Message;
                _logger.LogError(ex, "Failed to publish outbox message {Id} to {Topic}", msg.Id, msg.Topic);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
