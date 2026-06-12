using System;
using System.IO;
using System.Text.Json;
using BepInEx.Configuration;
using ByJP.AtprotoGaming.Core;
using ByJP.AtprotoGaming.Core.Adapters;

namespace ByJP.Ror2.Play.Config
{
    /// <summary>
    /// Backs the package's <see cref="IConfigStore"/> with BepInEx's config system. The
    /// player-facing <c>.cfg</c> holds only what they edit — handle + app password under
    /// <c>[Login]</c>, recording prefs under <c>[Recording]</c>. The auto-managed identity
    /// /stats cache the package writes back is kept in a sidecar JSON next to the outbox,
    /// NOT in the <c>.cfg</c>, so it doesn't clutter the mod manager's config editor.
    /// </summary>
    internal sealed class BepInExConfigStore : IConfigStore
    {
        private readonly Ror2PlayConfig _config = new Ror2PlayConfig();
        private readonly string _cachePath;

        private readonly ConfigEntry<string> _handle;
        private readonly ConfigEntry<string> _appPassword;
        private readonly ConfigEntry<string> _source;
        private readonly ConfigEntry<int> _throttleSeconds;
        private readonly ConfigEntry<string> _status;

        /// <summary>
        /// Raised when the player edits Handle or AppPassword (the conventional
        /// "save" signal). The plugin re-runs login so the credentials are checked
        /// live and <see cref="SetStatus"/> reflects the result.
        /// </summary>
        public event Action? CredentialsChanged;

        public BepInExConfigStore(ConfigFile file, IFileSystem fs)
        {
            _handle = file.Bind("Login", "Handle", "",
                "Your atproto handle, e.g. you.bsky.social (or a did:plc:…). Leave blank to disable publishing.");
            _appPassword = file.Bind("Login", "AppPassword", "",
                "An atproto APP PASSWORD — create one at https://bsky.app/settings/app-passwords. NOT your main password.");
            _status = file.Bind("Login", "Status", "",
                new ConfigDescription("Live login status — read-only, updated when you change your handle/app password.",
                    null, new ConfigurationManagerAttributes { ReadOnly = true }));
            _source = file.Bind("Recording", "Source", StatsSource.Steam,
                "The platform you play on: steam, epic, gog, playstation, xbox, nintendo, itchio or humble.");
            _throttleSeconds = file.Bind("Recording", "ThrottleSeconds", 60,
                "Minimum seconds between in-progress record updates while playing (game-over always writes immediately).");

            _config.Handle = _handle.Value;
            _config.AppPassword = _appPassword.Value;
            _config.Source = _source.Value;
            _config.ThrottleSeconds = _throttleSeconds.Value;
            // Game stays at the Ror2PlayConfig default — internal, not player-editable.

            _cachePath = Path.Combine(fs.OutboxRoot, "identity-cache.json");
            LoadCache();

            _handle.SettingChanged += OnCredentialChanged;
            _appPassword.SettingChanged += OnCredentialChanged;
        }

        /// <summary>The typed config the RoR2 layer reads (game/source/throttle).</summary>
        public Ror2PlayConfig Settings => _config;

        public CoreConfig Core => _config;

        public bool HasCredentials =>
            !string.IsNullOrEmpty(_config.Handle) && !string.IsNullOrEmpty(_config.AppPassword);

        /// <summary>Shows a human-readable login result in the read-only Status line.</summary>
        public void SetStatus(string status) => _status.Value = status;

        private void OnCredentialChanged(object sender, EventArgs e)
        {
            _config.Handle = _handle.Value;
            _config.AppPassword = _appPassword.Value;
            CredentialsChanged?.Invoke();
        }

        // The resolved identity + stats rkey the package writes back via Save(), persisted
        // beside the outbox so an offline boot and later runs reuse them. Not in the .cfg.
        private sealed class CacheState
        {
            public string CachedHandle { get; set; } = "";
            public string CachedDid { get; set; } = "";
            public string CachedPds { get; set; } = "";
            public string StatsRkey { get; set; } = "";
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(_cachePath)) return;
                var c = JsonSerializer.Deserialize<CacheState>(File.ReadAllText(_cachePath));
                if (c == null) return;
                _config.CachedHandle = c.CachedHandle;
                _config.CachedDid = c.CachedDid;
                _config.CachedPds = c.CachedPds;
                _config.StatsRkey = c.StatsRkey;
            }
            catch { /* a missing/corrupt cache just re-resolves on next login */ }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                File.WriteAllText(_cachePath, JsonSerializer.Serialize(new CacheState
                {
                    CachedHandle = _config.CachedHandle,
                    CachedDid = _config.CachedDid,
                    CachedPds = _config.CachedPds,
                    StatsRkey = _config.StatsRkey,
                }));
            }
            catch { /* non-fatal: the cache is an optimization, not correctness */ }
        }
    }
}
