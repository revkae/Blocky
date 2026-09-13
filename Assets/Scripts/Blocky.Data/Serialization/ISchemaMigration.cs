using Newtonsoft.Json.Linq;

namespace Blocky.Data.Serialization
{
    /// <summary>
    /// One version step, n -> n+1, applied to the raw JSON before deserialization (old shapes may not match
    /// current C# types). Ship each migration with a fixture of the old version and a round-trip test (TDD §5.2).
    /// </summary>
    public interface ISchemaMigration
    {
        int FromVersion { get; }
        JObject Migrate(JObject json);
    }
}
