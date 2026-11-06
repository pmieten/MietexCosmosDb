using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Mietex.CosmosDb;

/// <summary>
/// Newtonsoft.Json serializer for the Cosmos SDK. The Azure Cosmos DB SDK does not expose a default
/// serializer that supports custom <see cref="JsonSerializerSettings"/>.
/// </summary>
public sealed class CosmosJsonDotNetSerializer : CosmosLinqSerializer
{
    private readonly JsonSerializerSettings _serializerSettings;

    /// <summary>Initializes a serializer from Newtonsoft.Json settings.</summary>
    /// <param name="settings">JSON serializer settings.</param>
    public CosmosJsonDotNetSerializer(JsonSerializerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _serializerSettings = settings;
    }

    /// <summary>Gets the settings used by this serializer.</summary>
    public JsonSerializerSettings Settings => _serializerSettings;

    /// <inheritdoc />
    public override T FromStream<T>(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (typeof(Stream).IsAssignableFrom(typeof(T)))
        {
            return (T)(object)stream;
        }

        using (stream)
        using (var streamReader = new StreamReader(stream, Encoding.UTF8))
        using (var jsonReader = new JsonTextReader(streamReader))
        {
            var serializer = JsonSerializer.Create(_serializerSettings);
            return serializer.Deserialize<T>(jsonReader)!;
        }
    }

    /// <inheritdoc />
    public override string SerializeMemberName(MemberInfo memberInfo)
    {
        ArgumentNullException.ThrowIfNull(memberInfo);

        var jsonProperty = memberInfo.GetCustomAttribute<JsonPropertyAttribute>(inherit: true);
        if (jsonProperty is not null)
        {
            return jsonProperty.PropertyName ?? memberInfo.Name;
        }

        var declaringType = memberInfo.DeclaringType;
        var dataContract = declaringType?.GetCustomAttribute<DataContractAttribute>(inherit: true);
        if (dataContract is not null)
        {
            var dataMember = memberInfo.GetCustomAttribute<DataMemberAttribute>(inherit: true);
            if (dataMember is not null)
            {
                return dataMember.Name ?? memberInfo.Name;
            }
        }

        if (memberInfo.GetCustomAttribute<JsonExtensionDataAttribute>(inherit: true) is not null)
        {
            // Cosmos SDK treats null as "the member is not a serialized property".
            return null!;
        }

        return memberInfo.Name;
    }

    /// <inheritdoc />
    public override Stream ToStream<T>(T input)
    {
        var stream = new MemoryStream();
        using (var streamWriter = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true))
        using (var jsonWriter = new JsonTextWriter(streamWriter) { Formatting = Formatting.None })
        {
            var serializer = JsonSerializer.Create(_serializerSettings);
            serializer.Serialize(jsonWriter, input, typeof(T));
        }

        stream.Position = 0;
        return stream;
    }
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
        IEnumerable<JsonConverter>? converters = null)
    {
        var settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            ReferenceLoopHandling = ReferenceLoopHandling.Error,
            PreserveReferencesHandling = PreserveReferencesHandling.None,
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        };

        settings.Converters.Add(new StringEnumConverter());

        if (converters is not null)
        {
            foreach (var converter in converters)
            {
                settings.Converters.Add(converter);
            }
        }

        configure?.Invoke(settings);

        return settings;
    }
}
