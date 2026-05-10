using System.Text.Json.Nodes;

namespace FestivalRider.Migrators;

// Per plan 008: migrators are pure (JsonNode in -> JsonNode out), step-wise
// (ToVersion == FromVersion + 1), and frozen once shipped. The accumulator
// `warnings` list is the only observable side effect.
public interface IStateMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonNode Migrate(JsonNode raw, IList<string> warnings);
}
