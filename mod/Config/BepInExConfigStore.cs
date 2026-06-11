using BepInEx.Configuration;
using ByJP.AtprotoGaming.Core;

namespace ByJP.Ror2.Play.Config
{
    /// <summary>
    /// Backs the package's <see cref="IConfigStore"/> with BepInEx's config system, so
    /// the player edits their atproto handle + app password in the mod manager's config
    /// editor (or the <c>BepInEx/config/*.cfg</c>) — no hand-edited JSON. Credentials
    /// live under <c>[Login]</c>; the identity/stats cache the package writes back lives
    /// under <c>[Cache]</c> (auto-managed). One file, no duplicated secret.
    /// </summary>
    internal sealed class BepInExConfigStore : IConfigStore
    {
        private readonly Ror2PlayConfig _config = new Ror2PlayConfig();

        private readonly ConfigEntry<string> _handle;
        private readonly ConfigEntry<string> _appPassword;
        private readonly ConfigEntry<string> _source;
        private readonly ConfigEntry<int> _throttleSeconds;
        private readonly ConfigEntry<string> _cachedHandle;
        private readonly ConfigEntry<string> _cachedDid;
        private readonly ConfigEntry<string> _cachedPds;
        private readonly ConfigEntry<string> _statsRkey;

        public BepInExConfigStore(ConfigFile file)
        {
            _handle = file.Bind("Login", "Handle", "",
                "Your atproto handle, e.g. you.bsky.social (or a did:plc:…). Leave blank to disable publishing.");
            _appPassword = file.Bind("Login", "AppPassword", "",
                "An atproto APP PASSWORD — create one at https://bsky.app/settings/app-passwords. NOT your main password.");
            _source = file.Bind("Recording", "Source", StatsSource.Steam,
                "The platform you play on: steam, epic, gog, playstation, xbox, nintendo, itchio or humble.");
            _throttleSeconds = file.Bind("Recording", "ThrottleSeconds", 60,
                "Minimum seconds between in-progress record updates while playing (game-over always writes immediately).");

            _cachedHandle = file.Bind("Cache", "CachedHandle", "", "Managed automatically — do not edit.");
            _cachedDid = file.Bind("Cache", "CachedDid", "", "Managed automatically — do not edit.");
            _cachedPds = file.Bind("Cache", "CachedPds", "", "Managed automatically — do not edit.");
            _statsRkey = file.Bind("Cache", "StatsRkey", "", "Managed automatically — do not edit.");

            _config.Handle = _handle.Value;
            _config.AppPassword = _appPassword.Value;
            _config.Source = _source.Value;
            _config.ThrottleSeconds = _throttleSeconds.Value;
            _config.CachedHandle = _cachedHandle.Value;
            _config.CachedDid = _cachedDid.Value;
            _config.CachedPds = _cachedPds.Value;
            _config.StatsRkey = _statsRkey.Value;
            // Game stays at the Ror2PlayConfig default — internal, not player-editable.
        }

        /// <summary>The typed config the RoR2 layer reads (game/source/throttle).</summary>
        public Ror2PlayConfig Settings => _config;

        public CoreConfig Core => _config;

        public bool HasCredentials =>
            !string.IsNullOrEmpty(_config.Handle) && !string.IsNullOrEmpty(_config.AppPassword);

        public void Save()
        {
            // Only the cache fields change at runtime; persist them so an offline boot
            // and later runs reuse the resolved identity and the same stats record.
            _cachedHandle.Value = _config.CachedHandle;
            _cachedDid.Value = _config.CachedDid;
            _cachedPds.Value = _config.CachedPds;
            _statsRkey.Value = _config.StatsRkey;
        }
    }
}
