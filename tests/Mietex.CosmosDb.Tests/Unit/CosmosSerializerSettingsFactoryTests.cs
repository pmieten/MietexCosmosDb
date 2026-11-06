using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Mietex.CosmosDb.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CosmosSerializerSettingsFactoryTests
{
    [Fact]
    public void DefaultsMatchDocumentedCompatibilitySettings()
    {
        var settings = CosmosSerializerSettingsFactory.Create();

        Assert.Equal(TypeNameHandling.Auto, settings.TypeNameHandling);
        Assert.Equal(ReferenceLoopHandling.Error, settings.ReferenceLoopHandling);
        Assert.Equal(PreserveReferencesHandling.None, settings.PreserveReferencesHandling);
        Assert.Equal(ConstructorHandling.AllowNonPublicDefaultConstructor, settings.ConstructorHandling);
        var converter = Assert.Single(settings.Converters);
        Assert.IsType<StringEnumConverter>(converter);
    }

    [Fact]
    public void ExtraConvertersAreAppendedAfterDefaults()
    {
        var extra = new IsoDateTimeConverter();

        var settings = CosmosSerializerSettingsFactory.Create(converters: [extra]);

        Assert.Equal(2, settings.Converters.Count);
        Assert.IsType<StringEnumConverter>(settings.Converters[0]);
        Assert.Same(extra, settings.Converters[1]);
    }

    [Fact]
    public void ConfigureCallbackRunsAfterDefaultsAndExtraConverters()
    {
        var observed = new List<JsonConverter>();
        var extra = new IsoDateTimeConverter();
        var settings = CosmosSerializerSettingsFactory.Create(
            configure: configured =>
            {
                observed.AddRange(configured.Converters);
                configured.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                configured.Converters.Add(new StringEnumConverter());
            },
            converters: [extra]);

        Assert.Equal(2, observed.Count);
        Assert.IsType<StringEnumConverter>(observed[0]);
        Assert.Same(extra, observed[1]);
        Assert.Equal(ReferenceLoopHandling.Ignore, settings.ReferenceLoopHandling);
        Assert.Equal(3, settings.Converters.Count);
    }

    [Fact]
    public void DefaultsContainNoHostSpecificConverters()
    {
        var settings = CosmosSerializerSettingsFactory.Create();

        Assert.All(settings.Converters, converter => Assert.IsType<StringEnumConverter>(converter));
    }
}
