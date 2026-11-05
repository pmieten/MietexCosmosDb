using System.Reflection;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Mietex.CosmosDb;

/// <summary>
/// Newtonsoft.Json serializer for the Cosmos SDK. The Azure Cosmos DB SDK does not expose a default
/// serializer that supports custom <see cref="JsonSerializerSettings"/>.
/// </summary>
public sealed class CosmosJsonDotNetSerializer : CosmosLinqSerializer
{
    /// <summary>Initializes a serializer from Newtonsoft.Json settings.</summary>
    /// <param name="settings">JSON serializer settings.</param>
    public CosmosJsonDotNetSerializer(JsonSerializerSettings settings) =>
        throw new NotImplementedException();

    /// <summary>Gets the settings used by this serializer.</summary>
    public JsonSerializerSettings Settings => throw new NotImplementedException();

    /// <inheritdoc />
    public override T FromStream<T>(Stream stream) => throw new NotImplementedException();

    /// <inheritdoc />
    public override string SerializeMemberName(MemberInfo memberInfo) => throw new NotImplementedException();

    /// <inheritdoc />
    public override Stream ToStream<T>(T input) => throw new NotImplementedException();
}

/// <summary>Creates the default Newtonsoft.Json settings used by the library.</summary>
public static class CosmosSerializerSettingsFactory
{
    /// <summary>Creates settings with compatibility defaults and optional host converters.</summary>
    /// <param name="configure">Optional callback applied last.</param>
    /// <param name="converters">Optional extra converters.</param>
    /// <returns>New settings.</returns>
    public static JsonSerializerSettings Create(
        Action<JsonSerializerSettings>? configure = null,
        IEnumerable<JsonConverter>? converters = null) => throw new NotImplementedException();
}
