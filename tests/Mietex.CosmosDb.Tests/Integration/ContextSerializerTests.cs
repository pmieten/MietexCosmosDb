using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextSerializerTests : IntegrationContextTestBase
{
    public ContextSerializerTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-serializer";

    [Fact]
    public async Task EnumAndPolymorphicPayloadRoundTripThroughEmulator()
    {
        var partitionKey = NewPartition();
        var options = Emulator.CreateOptions(ContainerName, partitionKey);
        var text = new SerializerEntity
        {
            Kind = TestKind.Priority,
            Payload = new TextPayload { Label = "text", Text = "hello" },
        };
        var number = new SerializerEntity
        {
            Kind = TestKind.Standard,
            Payload = new NumericPayload { Label = "number", Number = 42 },
        };

        await using (var writer = new SerializerDbContext(options, TestUser()))
        {
            writer.Add(text);
            writer.Add(number);
            await writer.SaveChangesAsync(Cancellation);
        }

        await using var reader = new SerializerDbContext(options, TestUser());

        var readText = await reader.Entities.FindAsync(text.Id, Cancellation);
        Assert.NotNull(readText);
        Assert.Equal(text.Id, readText.Id);
        Assert.Equal(TestKind.Priority, readText.Kind);
        Assert.False(string.IsNullOrWhiteSpace(readText.Etag));
        var textPayload = Assert.IsType<TextPayload>(readText.Payload);
        Assert.Equal("hello", textPayload.Text);

        var readNumber = await reader.Entities.FindAsync(number.Id, Cancellation);
        Assert.NotNull(readNumber);
        Assert.Equal(TestKind.Standard, readNumber.Kind);
        var numberPayload = Assert.IsType<NumericPayload>(readNumber.Payload);
        Assert.Equal(42, numberPayload.Number);

        var raw = await reader.Container.ReadItemAsync<SerializerEntity>(
            text.Id,
            new PartitionKey(partitionKey),
            cancellationToken: Cancellation);
        Assert.Equal(text.Id, raw.Resource.Id);
        Assert.Equal(raw.ETag, readText.Etag);
        var rawPayload = Assert.IsType<TextPayload>(raw.Resource.Payload);
        Assert.Equal("hello", rawPayload.Text);
    }

    public abstract class TestPayload
    {
        public string Label { get; set; } = string.Empty;
    }

    public sealed class TextPayload : TestPayload
    {
        public string Text { get; set; } = string.Empty;
    }

    public sealed class NumericPayload : TestPayload
    {
        public int Number { get; set; }
    }

    public sealed class SerializerEntity : ICosmosEntity
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonProperty(nameof(PartitionKey))]
        public string PartitionKey { get; set; } = string.Empty;

        public string Discriminator { get; set; } = nameof(SerializerEntity);

        [JsonProperty("_etag")]
        public string? Etag { get; set; }

        public TestKind Kind { get; set; }

        public TestPayload? Payload { get; set; }

        public bool IsNew() => string.IsNullOrWhiteSpace(Etag);
    }

    public sealed class SerializerDbContext : CosmosDbContextBase<SerializerEntity, TestUser>
    {
        public SerializerDbContext(CosmosDbOptions options, TestUser? currentUser = null)
            : base(options, currentUser)
        {
        }

        public CosmosDbSet<SerializerEntity> Entities { get; set; } = null!;
    }
}
