namespace FestivalRider.BundleMigrators;

// Per plan 013: bundle migrators are pure (mutate a scratch model in place),
// step-wise (ToVersion == FromVersion + 1), and frozen once shipped. The
// `warnings` list is the only observable side effect besides the scratch.
public interface IBundleMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Migrate(BundleScratch scratch, IList<string> warnings);
}
