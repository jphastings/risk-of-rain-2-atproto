using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Logging;
using RoR2;
using UnityEngine;
using ByJP.AtprotoGaming.Core;
using ByJP.Ror2.Play.Config;
using ByJP.Ror2.Play.Mapping;

namespace ByJP.Ror2.Play.Ror2
{
    /// <summary>
    /// Owns the run lifecycle: opens a <see cref="PlaySession"/> at run start,
    /// flips a dirty bit on meaningful events, and emits a throttled snapshot
    /// through <see cref="PlayRecordMapper"/>. The package does no scheduling of its
    /// own, so the throttle/safety-net loop lives here as a Unity coroutine.
    /// </summary>
    internal sealed class RunTracker
    {
        private readonly AtprotoGamingClient _client;
        private readonly Ror2PlayConfig _config;
        private readonly ManualLogSource _log;
        private readonly string _modVersion;

        private PlayRecordMapper? _mapper;
        private readonly List<StageVisit> _stages = new List<StageVisit>();
        private StageVisit? _openStage;

        private volatile bool _dirty;
        private float _lastEmit;
        private bool _active;
        private string? _runId; // suppress duplicate onRunStartGlobal (save-load re-fires)
        private int _lastAchievementsUnlocked = -1; // skip the bulk profile-load re-fire

        public RunTracker(AtprotoGamingClient client, Ror2PlayConfig config, ManualLogSource log, string modVersion)
        {
            _client = client;
            _config = config;
            _log = log;
            _modVersion = modVersion;
        }

        public void Hook()
        {
            Run.onRunStartGlobal += OnRunStart;
            Stage.onStageStartGlobal += OnStageStart;
            Run.onServerGameOver += OnServerGameOver;
            Run.onClientGameOverGlobal += OnClientGameOver;
            Run.onRunDestroyGlobal += OnRunDestroy;
            On.RoR2.UserProfile.AddAchievement += OnAddAchievement; // VERIFY signature on build
        }

        /// <summary>Drives throttle + safety-net emission; started as a coroutine on the plugin.</summary>
        public IEnumerator EmitLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(5f);
                if (_active && _dirty && Time.unscaledTime - _lastEmit >= _config.ThrottleSeconds)
                    Emit(terminal: false);
            }
        }

        private void OnRunStart(Run run)
        {
            var id = run.seed.ToString() + "@" + run.GetStartTimeUtc().Ticks;
            if (_runId == id) return; // already tracking this run (re-fire on save-load)
            _runId = id;

            var startedAt = new DateTimeOffset(run.GetStartTimeUtc(), TimeSpan.Zero);
            var playId = PlaySession.DerivePlayID(startedAt, run.seed.ToString());
            var gameVersion = Application.version;
            var additional = new Dictionary<string, string> { [PluginInfo.Guid] = _modVersion };

            var session = _client.OpenPlay(playId, _config.Game, gameVersion, _config.Source, additional);
            _mapper = new PlayRecordMapper(_client, session, _config.Game, _config.Source);

            _stages.Clear();
            _openStage = null;
            _lastAchievementsUnlocked = -1;
            _active = true;
            _lastEmit = float.NegativeInfinity;
            _dirty = true;
            Emit(terminal: false); // create the record immediately
            _log.LogInfo($"tracking RoR2 play {playId}");
        }

        private void OnStageStart(Stage stage)
        {
            var sceneName = stage.sceneDef != null ? stage.sceneDef.cachedName : "unknown";
            CloseOpenStage();
            _openStage = new StageVisit { Id = sceneName, StartedAt = DateTimeOffset.UtcNow };
            _dirty = true;
        }

        private void OnServerGameOver(Run run, GameEndingDef ending) => Finish(run, ending);

        private void OnClientGameOver(Run run, RunReport report) => Finish(run, report?.gameEnding);

        private void Finish(Run run, GameEndingDef? ending)
        {
            if (!_active) return;
            CloseOpenStage();
            _pendingOutcome = ClassifyOutcome(ending);
            _pendingCause = ending != null ? ending.cachedName : null;
            _pendingEndedAt = DateTimeOffset.UtcNow;
            Emit(terminal: true);
        }

        private void OnRunDestroy(Run run)
        {
            _active = false;
            _mapper = null;
            _runId = null;
        }

        private RunOutcome? _pendingOutcome;
        private string? _pendingCause;
        private DateTimeOffset? _pendingEndedAt;

        private void Emit(bool terminal)
        {
            var mapper = _mapper;
            if (mapper == null || Run.instance == null) return;

            RunSnapshot snap;
            try
            {
                snap = StateExtractor.Capture(Run.instance); // main thread
            }
            catch (Exception ex)
            {
                _log.LogError($"snapshot capture failed: {ex}");
                return;
            }

            foreach (var stage in _stages) snap.Stages.Add(stage);
            if (terminal && _pendingOutcome != null)
            {
                snap.Outcome = _pendingOutcome;
                snap.OutcomeCause = _pendingCause;
                snap.EndedAt = _pendingEndedAt;
            }

            _dirty = false;
            _lastEmit = Time.unscaledTime;

            // The package is fully async and offline-safe; fire-and-forget off the
            // game thread. EmitAsync queues to the outbox if we're offline.
            _ = Task.Run(async () =>
            {
                try { await mapper.EmitAsync(snap).ConfigureAwait(false); }
                catch (Exception ex) { _log.LogError($"play emit failed: {ex.Message}"); }
            });
        }

        private void CloseOpenStage()
        {
            if (_openStage == null) return;
            _openStage.EndedAt = DateTimeOffset.UtcNow;
            _stages.Add(_openStage);
            _openStage = null;
        }

        private void OnAddAchievement(On.RoR2.UserProfile.orig_AddAchievement orig, UserProfile self, string achievementName)
        {
            orig(self, achievementName);
            if (!_active) return;

            // The stats record stores counts (unlocked/total), not per-achievement
            // entries — so recompute the totals and push them. Skip when the unlocked
            // count is unchanged so the bulk profile-load re-fire doesn't spam.
            if (!StateExtractor.TryGetAchievementCounts(out var unlocked, out var total)) return;
            if (unlocked == _lastAchievementsUnlocked) return;
            _lastAchievementsUnlocked = unlocked;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _client.Stats.AchievementsUnlockedAsync(_config.Game, _config.Source, unlocked, total)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) { _log.LogError($"achievement count publish failed: {ex.Message}"); }
            });
        }

        private static RunOutcome ClassifyOutcome(GameEndingDef? ending)
        {
            if (ending == null) return RunOutcome.Abandoned;
            switch (ending.cachedName)
            {
                case "MainEnding":
                case "VoidEnding":
                    return RunOutcome.Succeeded;
                default:
                    return RunOutcome.Failed;
            }
        }
    }
}
