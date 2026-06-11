using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using ByJP.AtprotoGaming.Core;
using ByJP.AtprotoGaming.Core.Adapters;
using ByJP.Ror2.Play.Adapters;
using ByJP.Ror2.Play.Config;
using ByJP.Ror2.Play.Ror2;

namespace ByJP.Ror2.Play
{
    internal static class PluginInfo
    {
        public const string Guid = "me.byjp.pesos.ror2.play";
        public const string Name = "ByJP RoR2 atproto Play";
        public const string Version = "0.1.0";

        // Set by CI when an embedded signing key is provided (see the .csproj).
        public const string AttestationType = "me.byjp.pesos.ror2.play#attestation";
    }

    /// <summary>
    /// BepInEx entry point. Wires the package's three adapters, loads config,
    /// constructs the <see cref="AtprotoGamingClient"/>, kicks off login on a
    /// background task, and hands the run lifecycle to <see cref="RunTracker"/>.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private AtprotoGamingClient _client = null!;
        private RunTracker _tracker = null!;

        private void Awake()
        {
            var log = new BepInExLogSink(Logger);

            // Credentials + cache live in the BepInEx config (.cfg, editable in the mod
            // manager); the offline outbox/ still lives next to this DLL.
            var fs = FileSystem.NextTo<Plugin>();
            var config = new BepInExConfigStore(Config);

            if (!config.HasCredentials)
                Logger.LogWarning(
                    "atproto publishing is OFF — set Handle and AppPassword in this mod's config " +
                    "(BepInEx/config/me.byjp.pesos.ror2.play.cfg, or the mod manager's config editor).");

            _client = new AtprotoGamingClient(new AtprotoGamingOptions
            {
                FileSystem = fs,
                Log = log,
                Config = config,
                SigningKey = LoadSigningKey(),
                PackageVersionOverride = null,
            });

            // Surface every login transition in the read-only config Status line (live
            // ✓/✗ feedback in the in-game config menu) and the BepInEx log.
            _client.Auth.Changed += () =>
            {
                var line = DescribeAuth(_client.Auth);
                config.SetStatus(line);
                Logger.LogInfo($"atproto: {line}");
            };

            // Re-check the credentials the moment the player edits them (the config
            // system's "save" signal) — they get an immediate ✓/✗ instead of guessing.
            config.CredentialsChanged += () => Task.Run(_client.LoginAsync);

            // No engine threads in the package — we own the task.
            _ = Task.Run(_client.LoginAsync);

            _tracker = new RunTracker(_client, config.Settings, Logger, PluginInfo.Version);
            _tracker.Hook();
            StartCoroutine(_tracker.EmitLoop());

            Logger.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded");
        }

        /// <summary>A short, player-facing description of the current login state.</summary>
        private static string DescribeAuth(AuthState auth)
        {
            switch (auth.Status)
            {
                case AuthStatus.Unconfigured: return "not configured — enter your handle and an app password";
                case AuthStatus.Checking: return "checking credentials…";
                case AuthStatus.Ok: return $"✓ signed in as {auth.Handle}";
                case AuthStatus.Failed: return $"✗ rejected: {auth.Error}";
                case AuthStatus.Offline: return $"offline — will retry; runs queue under {auth.Handle}";
                default: return auth.Status.ToString();
            }
        }

        /// <summary>
        /// Loads an optional P-256 signing key embedded at build time (CI secret),
        /// matching the sts2.at convention. Absent → records publish unsigned.
        /// </summary>
        private static SigningKey? LoadSigningKey()
        {
            var asm = Assembly.GetExecutingAssembly();
            var resource = asm.GetManifestResourceName_OrNull("signing-private-key.txt");
            if (resource == null) return null;

            using var stream = asm.GetManifestResourceStream(resource);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            var didKey = reader.ReadToEnd().Trim();
            if (string.IsNullOrEmpty(didKey)) return null;

            return SigningKey.FromDidKey(didKey, PluginInfo.AttestationType);
        }
    }

    internal static class AssemblyExtensions
    {
        public static string? GetManifestResourceName_OrNull(this Assembly asm, string endsWith)
        {
            foreach (var name in asm.GetManifestResourceNames())
                if (name.EndsWith(endsWith, StringComparison.Ordinal))
                    return name;
            return null;
        }
    }
}
