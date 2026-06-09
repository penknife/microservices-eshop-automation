namespace EShop.Messaging.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "default-group";
    public int OutboxPollingIntervalSeconds { get; set; } = 2;
    public int OutboxBatchSize { get; set; } = 50;
}
