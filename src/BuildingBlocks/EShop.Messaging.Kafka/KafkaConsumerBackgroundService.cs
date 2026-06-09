using System.Text.Json;
using Confluent.Kafka;
using EShop.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EShop.Messaging.Kafka;

/// <summary>
/// Subscribes to <paramref name="TEvent"/>'s topic, deserializes messages and dispatches
/// them to <paramref name="THandler"/>. Offsets are committed only after the handler returns
/// successfully, providing at-least-once delivery (idempotency handled by the consumer).
/// </summary>
public sealed class KafkaConsumerBackgroundService<TEvent, THandler> : BackgroundService
    where TEvent : class, IIntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KafkaOptions> _options;
    private readonly ILogger<KafkaConsumerBackgroundService<TEvent, THandler>> _logger;
    private readonly string _topic;

    public KafkaConsumerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<KafkaConsumerBackgroundService<TEvent, THandler>> logger,
        string topic)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _topic = topic;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var opts = _options.Value;
        var config = new ConsumerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            GroupId = opts.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = false,
            AllowAutoCreateTopics = true,
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogError("Kafka consumer error: {Reason}", e.Reason))
            .Build();

        consumer.Subscribe(_topic);
        _logger.LogInformation(
            "Kafka consumer subscribed to {Topic} as group {GroupId}", _topic, opts.ConsumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed on {Topic}", _topic);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                try
                {
                    var @event = JsonSerializer.Deserialize<TEvent>(result.Message.Value, s_jsonOptions);
                    if (@event is null)
                    {
                        _logger.LogWarning(
                            "Received null/empty payload on {Topic} partition {Partition} offset {Offset}",
                            result.Topic, result.Partition.Value, result.Offset.Value);
                        consumer.Commit(result);
                        continue;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                    await handler.HandleAsync(@event, stoppingToken).ConfigureAwait(false);

                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Handler {Handler} failed for {Topic} partition {Partition} offset {Offset}; will retry",
                        typeof(THandler).Name, result.Topic, result.Partition.Value, result.Offset.Value);

                    // Do not commit; back off briefly before letting Kafka re-deliver this offset.
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            try { consumer.Close(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error closing Kafka consumer"); }
        }
    }
}
