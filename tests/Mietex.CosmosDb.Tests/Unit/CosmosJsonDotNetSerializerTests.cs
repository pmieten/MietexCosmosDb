using System.Runtime.Serialization;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Mietex.CosmosDb.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CosmosJsonDotNetSerializerTests
{
    [Fact]
    public void ToStreamReturnsSeekableUtf8StreamAtStartWithoutBomOrIndentation()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        var entity = new SerializedEntity { Name = "widget", Value = 7 };

        using var stream = serializer.ToStream(entity);

        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Position);

        var bytes = ReadAllBytes(stream);
        Assert.False(StartsWithUtf8Bom(bytes));

        var json = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
        Assert.Equal("widget", (string?)JObject.Parse(json)["Name"]);
    }

    [Fact]
    public void EnumIsWrittenAsStringWhenStringEnumConverterIsConfigured()
    {
        var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
        var serializer = CreateSerializer(settings);
        var entity = new SerializedEntity { Kind = TestKind.Priority };

        using var stream = serializer.ToStream(entity);

        var json = Encoding.UTF8.GetString(ReadAllBytes(stream));
        Assert.Equal("Priority", (string?)JObject.Parse(json)["Kind"]);
    }

    [Fact]
    public void FromStreamRoundTripsSerializedObject()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        var original = new SerializedEntity
        {
            Name = "widget",
            Value = 42,
            Kind = TestKind.Priority,
        };

        using var stream = serializer.ToStream(original);
        var result = serializer.FromStream<SerializedEntity>(stream);

        Assert.Equal(original.Name, result.Name);
        Assert.Equal(original.Value, result.Value);
        Assert.Equal(original.Kind, result.Kind);
    }

    [Fact]
    public void FromStreamDisposesInput()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        var json = Encoding.UTF8.GetBytes("{\"Name\":\"widget\",\"Value\":3}");
        using var stream = new MemoryStream(json);

        var result = serializer.FromStream<SerializedEntity>(stream);

        Assert.Equal("widget", result.Name);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public void FromStreamReturnsTheSameStreamForStreamType()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        using var stream = new MemoryStream([1, 2, 3]);

        var result = serializer.FromStream<Stream>(stream);

        Assert.Same(stream, result);
        Assert.True(result.CanRead);
        Assert.Equal(3L, result.Length);
    }

    [Fact]
    public void SerializeMemberNameUsesJsonPropertyName()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        var member = typeof(JsonPropertyEntity).GetProperty(nameof(JsonPropertyEntity.Value))!;

        Assert.Equal("json_name", serializer.SerializeMemberName(member));
    }

    [Fact]
    public void SerializeMemberNameUsesDataMemberName()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        var member = typeof(DataContractEntity).GetProperty(nameof(DataContractEntity.Value))!;

        Assert.Equal("data_name", serializer.SerializeMemberName(member));
    }

    [Fact]
    public void SerializeMemberNameReturnsNullForJsonExtensionData()
    {
        var serializer = CreateSerializer(new JsonSerializerSettings());
        var member = typeof(ExtensionDataEntity).GetProperty(nameof(ExtensionDataEntity.Extra))!;

        Assert.Null(serializer.SerializeMemberName(member));
    }

    [Fact]
    public void ReferenceLoopErrorSettingMakesSerializationThrow()
    {
        var settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Error };
        var serializer = CreateSerializer(settings);
        var parent = CreateReferenceLoop();

        Assert.Throws<JsonSerializationException>(() => serializer.ToStream(parent));
    }

    [Fact]
    public void ReferenceLoopIgnoreSettingAllowsSerialization()
    {
        var settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
        var serializer = CreateSerializer(settings);
        var parent = CreateReferenceLoop();

        using var stream = serializer.ToStream(parent);

        var json = Encoding.UTF8.GetString(ReadAllBytes(stream));
        Assert.Contains("parent", json);
    }

    [Fact]
    public void FactorySettingsRoundTripPolymorphicPayloadWithTypeMetadata()
    {
        var serializer = CreateSerializer(CosmosSerializerSettingsFactory.Create());
        BasePayload original = new DerivedPayload { Name = "derived", Extra = 7 };

        using var stream = serializer.ToStream(original);
        var json = Encoding.UTF8.GetString(ReadAllBytes(stream));
        Assert.Contains("$type", json);

        stream.Position = 0;
        var result = serializer.FromStream<BasePayload>(stream);

        var derived = Assert.IsType<DerivedPayload>(result);
        Assert.Equal("derived", derived.Name);
        Assert.Equal(7, derived.Extra);
    }

    private static CosmosJsonDotNetSerializer CreateSerializer(JsonSerializerSettings settings) => new(settings);

    private static byte[] ReadAllBytes(Stream stream)
    {
        stream.Position = 0;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static bool StartsWithUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private static LoopNode CreateReferenceLoop()
    {
        var parent = new LoopNode { Name = "parent" };
        var child = new LoopNode { Name = "child", Parent = parent };
        parent.Children.Add(child);
        return parent;
    }

    private enum TestKind
    {
        Standard = 0,
        Priority = 1,
    }

    private sealed class SerializedEntity
    {
        public string Name { get; set; } = string.Empty;

        public int Value { get; set; }

        public TestKind Kind { get; set; }
    }

    private sealed class JsonPropertyEntity
    {
        [JsonProperty("json_name")]
        public string Value { get; set; } = string.Empty;
    }

    [DataContract]
    private sealed class DataContractEntity
    {
        [DataMember(Name = "data_name")]
        public string Value { get; set; } = string.Empty;
    }

    private sealed class ExtensionDataEntity
    {
        [JsonExtensionData]
        public Dictionary<string, object> Extra { get; set; } = [];
    }

    private class BasePayload
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DerivedPayload : BasePayload
    {
        public int Extra { get; set; }
    }

    private sealed class LoopNode
    {
        public string Name { get; set; } = string.Empty;

        public LoopNode? Parent { get; set; }

        public List<LoopNode> Children { get; } = [];
    }
}
