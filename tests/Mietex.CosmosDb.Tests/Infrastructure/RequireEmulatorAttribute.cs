using System.Reflection;
using Xunit.v3;

namespace Mietex.CosmosDb.Tests.Infrastructure;

/// <summary>
/// Skips an integration test when the Cosmos DB emulator was not reachable during fixture
/// initialization. The skip decision is made here, not in the collection fixture, because xUnit
/// reports an <c>Assert.Skip</c> from fixture initialization as a failure.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireEmulatorAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        Assert.SkipWhen(
            !CosmosEmulatorFixture.IsAvailable,
            CosmosEmulatorFixture.SkipReason ?? "Cosmos DB emulator is not available.");
    }
}
