using Confluent.Kafka;
using MemoryPack;

internal class KafkaMemoryPackSerDes<T>: ISerializer<T>, IDeserializer<T>
{
    public byte[] Serialize(T data, SerializationContext context) => MemoryPackSerializer.Serialize(data);
    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) => isNull ? default! : MemoryPackSerializer.Deserialize<T>(data)!;
}
