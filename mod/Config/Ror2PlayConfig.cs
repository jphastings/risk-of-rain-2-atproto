using System.Text.Json.Serialization;
using ByJP.AtprotoGaming.Core;

namespace ByJP.Ror2.Play.Config
{
    /// <summary>
    /// The mod's config DTO. Inherits the package's handle/app-password/cached
    /// identity/stats-rkey fields and adds RoR2-specific knobs.
    /// </summary>
    internal sealed class Ror2PlayConfig : CoreConfig
    {
        /// <summary>
        /// AT URI of the RoR2 game record in the games.gamesgamesgamesgames
        /// catalogue. Placeholder until a real one is registered with Birbhouse.
        /// </summary>
        [JsonPropertyName("game")]
        public string Game { get; set; } =
            "at://did:web:gamesgamesgamesgames.games/games.gamesgamesgamesgames.game/ror2";

        /// <summary>Platform this install runs on (see <see cref="StatsSource"/>).</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = StatsSource.Steam;

        /// <summary>Minimum seconds between in-progress snapshots (game-over always emits).</summary>
        [JsonPropertyName("throttleSeconds")]
        public int ThrottleSeconds { get; set; } = 60;
    }
}
