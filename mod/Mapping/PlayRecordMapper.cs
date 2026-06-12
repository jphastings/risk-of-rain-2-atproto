using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ByJP.AtprotoGaming.Core;

namespace ByJP.Ror2.Play.Mapping
{
    /// <summary>
    /// Turns <see cref="RunSnapshot"/>s into changes on the player's
    /// <c>games.gamesgamesgamesgames.actor.play</c> record via the package's
    /// <see cref="PlaySession"/>. One mapper per run.
    /// </summary>
    /// <remarks>
    /// With instance-keyed, replace-style ops the mapper holds no per-emit
    /// bookkeeping: it re-states the full acquisitions list and re-arrives/leaves
    /// every route stop each snapshot, and the package dedupes — so a mid-run crash
    /// can't duplicate entries.
    /// </remarks>
    public sealed class PlayRecordMapper
    {
        private readonly AtprotoGamingClient _client;
        private readonly PlaySession _play;
        private readonly string _game;
        private readonly string _source;
        private bool _settingsEmitted;
        private bool _characterEmitted;

        public PlayRecordMapper(AtprotoGamingClient client, PlaySession play, string game, string source)
        {
            _client = client;
            _play = play;
            _game = game;
            _source = source;
        }

        /// <summary>Writes the current run state as one record update (and rolls stats on the terminal one).</summary>
        public async Task<PutResult> EmitAsync(RunSnapshot snap)
        {
            var tx = _play.BeginUpdate();

            if (!_settingsEmitted)
            {
                tx.SetSetting("seed", snap.Seed);
                tx.SetSetting("mode", snap.Mode);
                if (snap.Difficulty is int difficulty) tx.SetSetting("difficulty", difficulty);
                if (snap.Artifacts.Count > 0)
                {
                    var artifacts = new JsonArray();
                    foreach (var artifact in snap.Artifacts) artifacts.Add(artifact);
                    tx.SetSetting("artifacts", artifacts);
                }
                _settingsEmitted = true;
            }

            // "character" = who you started as. The body isn't spawned on the very first
            // snapshot, so emit it once it's first known (not gated on _settingsEmitted).
            if (!_characterEmitted && !string.IsNullOrEmpty(snap.Character))
            {
                tx.SetSetting("character", snap.Character!);
                _characterEmitted = true;
            }

            // RoR2's StatSheet is authoritative and absolute, so set values directly.
            tx.SetProgress("stopwatch", snap.StopwatchSeconds);
            tx.SetProgress("stageClearCount", snap.StageClearCount);
            if (snap.CurrentHp is int hp) tx.SetProgress("hp", hp);
            if (snap.CurrentLevel is int level) tx.SetProgress("level", level);
            // current body (raw "<Survivor>Body", matching the per-body stat keys) —
            // distinct from settings.character once bodies change mid-run.
            if (!string.IsNullOrEmpty(snap.Character)) tx.SetProgress("character", snap.Character!);
            foreach (var stat in snap.Stats)
                tx.SetProgress(stat.Key, stat.Value);
            foreach (var map in snap.StatMaps)
                tx.SetProgress(map.Key, ToObject(map.Value)); // nested object via AtValue(JsonNode)

            EmitAcquisitions(tx, snap);
            EmitRoute(tx, snap);
            await EmitParticipantsAsync(tx, snap).ConfigureAwait(false);

            if (snap.Outcome is RunOutcome outcome)
            {
                tx.SetOutcome(OutcomeType(outcome), snap.OutcomeCause);
                var ended = snap.EndedAt ?? snap.StartedAt.AddSeconds(snap.StopwatchSeconds);
                tx.Finish(ended.ToString("o"), snap.StopwatchSeconds);
            }

            var result = await tx.CommitAsync().ConfigureAwait(false);

            if (snap.Outcome != null && result.Status == PutStatus.Published)
                await RollStatsAsync(snap).ConfigureAwait(false);

            return result;
        }

        private static void EmitAcquisitions(PlayUpdate tx, RunSnapshot snap)
        {
            // Replace the whole list each snapshot — idempotent, crash-safe.
            var items = new List<JsonObject>(snap.Items.Count);
            for (var i = 0; i < snap.Items.Count; i++)
            {
                var item = snap.Items[i];
                var node = new JsonObject { ["id"] = item.Id, ["instanceId"] = i.ToString() };
                if (item.Kind != null) node["kind"] = item.Kind;
                if (item.Name != null) node["name"] = item.Name;
                if (item.Count > 0) node["useCount"] = item.Count;
                node["addedAt"] = item.AddedAt.ToString("o");
                items.Add(node);
            }
            tx.SetAcquisitions(items);
        }

        private static void EmitRoute(PlayUpdate tx, RunSnapshot snap)
        {
            // Re-arrive/leave every stop each snapshot; instanceId (the visit ordinal,
            // stable across loops/revisits) makes it idempotent and lets the current
            // open stage appear immediately rather than lagging a stage behind.
            for (var i = 0; i < snap.Stages.Count; i++)
            {
                var stop = snap.Stages[i];
                var instanceId = i.ToString();
                tx.RouteArrive(stop.Id, instanceId, stop.Name, stop.StartedAt);
                if (stop.EndedAt is DateTimeOffset leftAt)
                    tx.RouteLeave(stop.Id, instanceId, leftAt);
            }
        }

        private async Task EmitParticipantsAsync(PlayUpdate tx, RunSnapshot snap)
        {
            if (snap.Allies.Count == 0) return;

            var participants = new List<JsonObject>(snap.Allies.Count);
            foreach (var ally in snap.Allies)
            {
                var participant = new JsonObject { ["steam"] = ally.Steam };
                var did = await _client.Steam.LookupDidAsync(ally.Steam).ConfigureAwait(false);
                if (did != null) participant["atproto"] = did;
                participants.Add(participant);
            }
            tx.SetPlayingWith(participants);
        }

        private async Task RollStatsAsync(RunSnapshot snap)
        {
            try
            {
                var ended = (snap.EndedAt ?? DateTimeOffset.UtcNow).ToString("o");
                await _client.Stats.EnsureAndUpdateAsync(_game, _source, snap.StopwatchSeconds, ended)
                    .ConfigureAwait(false);
            }
            catch
            {
                // best-effort: a stats failure must not lose the run record
            }
        }

        private static JsonObject ToObject(Dictionary<string, long> map)
        {
            var node = new JsonObject();
            foreach (var kv in map) node[kv.Key] = kv.Value;
            return node;
        }

        private static string OutcomeType(RunOutcome outcome)
        {
            switch (outcome)
            {
                case RunOutcome.Succeeded: return "succeeded";
                case RunOutcome.Abandoned: return "abandoned";
                default: return "failed";
            }
        }
    }
}
