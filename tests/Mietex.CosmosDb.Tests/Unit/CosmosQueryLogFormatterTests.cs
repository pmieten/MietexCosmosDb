using Microsoft.Azure.Cosmos;

namespace Mietex.CosmosDb.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class CosmosQueryLogFormatterTests
{
    [Fact]
    public void StringParameterIsExpandedAsQuotedText()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Name = @name")
            .WithParameter("@name", "widget");

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("'widget'", formatted);
        Assert.DoesNotContain("@name", formatted);
    }

    [Fact]
    public void DateTimeParameterIsExpandedAsReadableTimestamp()
    {
        var value = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.CreatedDate >= @created")
            .WithParameter("@created", value);

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("2026-03-04T05:06:07", formatted);
        Assert.DoesNotContain("@created", formatted);
    }

    [Fact]
    public void DateTimeOffsetParameterIsExpandedAsReadableTimestampWithOffset()
    {
        var value = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));
        var query = new QueryDefinition("SELECT * FROM c WHERE c.UpdatedDate <= @updated")
            .WithParameter("@updated", value);

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("2026-03-04T05:06:07", formatted);
        Assert.Contains("+02:00", formatted);
        Assert.DoesNotContain("@updated", formatted);
    }

    [Fact]
    public void NullParameterIsExpandedAsNullLiteral()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.DeletedDate = @deleted")
            .WithParameter("@deleted", null!);

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("null", formatted);
        Assert.DoesNotContain("@deleted", formatted);
    }

    [Fact]
    public void NumericParameterIsExpandedAsNumber()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Value > @count")
            .WithParameter("@count", 42);

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("42", formatted);
        Assert.DoesNotContain("@count", formatted);
    }

    [Fact]
    public void MultipleParametersAreAllReplaced()
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.Name = @name AND c.Kind = @kind AND c.Value > @minimum")
            .WithParameter("@name", "widget")
            .WithParameter("@kind", "Priority")
            .WithParameter("@minimum", 10);

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("'widget'", formatted);
        Assert.Contains("'Priority'", formatted);
        Assert.Contains("10", formatted);
        Assert.DoesNotContain("@name", formatted);
        Assert.DoesNotContain("@kind", formatted);
        Assert.DoesNotContain("@minimum", formatted);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("SELECT * FROM c WHERE")]
    public void QueryWithoutParametersIsReturnedUnchanged(string queryText)
    {
        var query = new QueryDefinition(queryText);

        Assert.Equal(queryText, CosmosQueryLogFormatter.Format(query));
    }

    [Fact]
    public void SingleQuotesAreEscaped()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Name = @name")
            .WithParameter("@name", "O'Brien");

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("'O''Brien'", formatted);
    }

    [Fact]
    public void ControlCharactersAreEscapedForLogSafety()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Name = @name")
            .WithParameter("@name", "line1\nline2\r\t");

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.DoesNotContain("\n", formatted);
        Assert.DoesNotContain("\r", formatted);
        Assert.DoesNotContain("\t", formatted);
        Assert.Contains("\\n", formatted);
        Assert.Contains("\\r", formatted);
        Assert.Contains("\\t", formatted);
    }

    [Fact]
    public void PrefixParameterNamesAreReplacedWithoutCorruptingLongerNames()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Id = @id OR c.Other = @id2")
            .WithParameter("@id", "first")
            .WithParameter("@id2", "second");

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("'first'", formatted);
        Assert.Contains("'second'", formatted);
        Assert.DoesNotContain("@id", formatted);
    }

    [Fact]
    public void UnformattableValuesDoNotBreakFormatting()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Value = @value")
            .WithParameter("@value", new ThrowingValue());

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.Contains("<unformattable>", formatted);
    }

    [Fact]
    public void FormattingDoesNotChangeTheQueryDefinition()
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Name = @name")
            .WithParameter("@name", "widget");
        var originalQueryText = query.QueryText;

        var formatted = CosmosQueryLogFormatter.Format(query);

        Assert.NotEmpty(formatted);
        Assert.Equal(originalQueryText, query.QueryText);
    }

    private sealed class ThrowingValue
    {
        public override string ToString() => throw new InvalidOperationException("boom");
    }
}
