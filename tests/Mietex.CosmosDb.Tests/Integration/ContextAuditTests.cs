using Microsoft.Extensions.Time.Testing;

namespace Mietex.CosmosDb.Tests.Integration;

[Collection(CosmosEmulatorCollectionDefinition.Name)]
[RequireEmulator]
[Trait("Category", "Integration")]
public sealed class ContextAuditTests : IntegrationContextTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public ContextAuditTests(CosmosEmulatorFixture emulator)
        : base(emulator)
    {
    }

    protected override string ContainerName => "context-audit";

    [Fact]
    public async Task NewEntityIsStampedWithCreatedAndUpdatedAudit()
    {
        var clock = new FakeTimeProvider(Start);
        var options = Options(NewPartition(), o => o with { TimeProvider = clock });
        var user = TestUser("alice");
        var entity = new TestEntity { Name = "new" };

        await using (var context = new TestDbContext(options, user))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(Start, entity.CreatedDate);
        Assert.Equal(user, entity.CreatedById);
        Assert.Equal(Start, entity.UpdatedDate);
        Assert.Equal(user, entity.UpdatedById);
    }

    [Fact]
    public async Task ExistingEntityUpdatesUpdatedFieldsButKeepsCreatedAudit()
    {
        var clock = new FakeTimeProvider(Start);
        var options = Options(NewPartition(), o => o with { TimeProvider = clock });
        var user = TestUser("alice");
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "before" }, user);

        clock.Advance(TimeSpan.FromMinutes(5));
        entity.Name = "after";

        await using (var context = new TestDbContext(options, user))
        {
            context.Add(entity);
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(Start, entity.CreatedDate);
        Assert.Equal(user, entity.CreatedById);
        Assert.Equal(Start.AddMinutes(5), entity.UpdatedDate);
        Assert.Equal(user, entity.UpdatedById);
    }

    [Fact]
    public async Task SkipUserIdStillStampsCreatedDateButLeavesUpdatedFieldsUntouched()
    {
        var clock = new FakeTimeProvider(Start);
        var options = Options(NewPartition(), o => o with { TimeProvider = clock });
        var user = TestUser("alice");
        var entity = new TestEntity { Name = "new" };

        await using (var context = new TestDbContext(options, user))
        {
            context.Add(entity, skipUserId: true);
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(Start, entity.CreatedDate);
        Assert.Null(entity.CreatedById);
        Assert.Equal(default(DateTimeOffset), entity.UpdatedDate);
        Assert.Null(entity.UpdatedById);
    }

    [Fact]
    public async Task SkipUserIdOnExistingEntityPreservesUpdatedAudit()
    {
        var clock = new FakeTimeProvider(Start);
        var options = Options(NewPartition(), o => o with { TimeProvider = clock });
        var user = TestUser("alice");
        var entity = await CreateSeedAsync(options, new TestEntity { Name = "before" }, user);

        clock.Advance(TimeSpan.FromHours(1));
        entity.Name = "after";

        await using (var context = new TestDbContext(options, user))
        {
            context.Add(entity, skipUserId: true);
            await context.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(Start, entity.CreatedDate);
        Assert.Equal(user, entity.CreatedById);
        Assert.Equal(Start, entity.UpdatedDate);
        Assert.Equal(user, entity.UpdatedById);
    }

    [Fact]
    public void RequireUserForAuditedWritesRejectsMissingUserUnlessSkipUserIdIsSet()
    {
        var options = Options(
            NewPartition(),
            o => o with { RequireUserForAuditedWrites = true });
        using var context = new TestDbContext(options);

        Assert.Throws<InvalidOperationException>(() => context.Add(new TestEntity()));

        var skipped = new TestEntity();
        var exception = Record.Exception(() => context.Add(skipped, skipUserId: true));

        Assert.Null(exception);
    }
}
