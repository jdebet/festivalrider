using System.Text.Json.Nodes;
using FestivalRider.Migrators;

namespace FestivalRider.Tests.Migrators;

internal sealed class ThrowingMigrator : IStateMigrator
{
    public int FromVersion { get; init; } = 1;
    public int ToVersion => FromVersion + 1;
    public JsonNode Migrate(JsonNode raw, IList<string> warnings) =>
        throw new InvalidOperationException("boom");
}
