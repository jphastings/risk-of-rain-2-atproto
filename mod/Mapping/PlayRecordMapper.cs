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
    /// This is the layer that exercises the package API. Notable frictions found
    /// while writing it are flagged with <c>GAP:</c> comments and collected in
    /// <c>docs/api-gaps.md</c>.
    /// </remarks>
    public sealed class PlayRecordMapper
    {
        private readonly AtprotoGamingClient _client;
        private readonly PlaySession _play;
        private readonly string _game;
        private readonly string _source;

        // GAP: AddAcquisition / AddRouteStop are append-only and the mapper has no
        // way to read what's already on the record, so it must remember how much it
        // has emitted to avoid re-appending. (See docs/api-gaps.md #1, #3.)
        private int _emittedItems;
        private readonly HashSet<string> _emittedStops = new HashSet<string>();
        private bool _settingsEmitted;

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
                if (!string.IsNullOrEmpty(snap.Character)) tx.SetSetting("character", snap.Character!);
                _settingsEmitted = true;
            }

            // Progress: RoR2's StatSheet is authoritative and absolute, so we set
            // values rather than increment. (IncrementProgress is unused here — see
            // docs/api-gaps.md #5.)
            tx.SetProgress("stopwatch", snap.StopwatchSeconds);
            tx.SetProgress("stageClearCount", snap.StageClearCount);
            if (snap.CurrentHp is int hp) tx.SetProgress("hp", hp);
            if (snap.CurrentLevel is int level) tx.SetProgress("level", level);
            foreach (var stat in snap.Stats)
                tx.SetProgress(stat.Key, stat.Value);
            foreach (var map in snap.StatMaps)
                tx.SetProgress(map.Key, ToObject(map.Value)); // nested object via AtValue(JsonNode)

            EmitNewAcquisitions(tx, snap);
            EmitCompletedRouteStops(tx, snap);
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

        private void EmitNewAcquisitions(PlayUpdate tx, RunSnapshot snap)
        {
            for (var i = _emittedItems; i < snap.Items.Count; i++)
            {
                var item = snap.Items[i];
                var node = new JsonObject { ["id"] = item.Id };
                if (item.Kind != null) node["kind"] = item.Kind;
                if (item.Name != null) node["name"] = item.Name;
                node["addedAt"] = item.AddedAt.ToString("o");
                tx.AddAcquisition(node);
            }
            _emittedItems = snap.Items.Count;
        }

        private void EmitCompletedRouteStops(PlayUpdate tx, RunSnapshot snap)
        {
            // GAP: a route stop's endedAt isn't known until the player leaves it,
            // and there's no helper to update an already-appended stop, so we only
            // emit stops once they're complete. (docs/api-gaps.md #3.)
            foreach (var stop in snap.Stages)
            {
                if (stop.EndedAt is null) continue;
                var key = stop.Id + "@" + stop.StartedAt.ToString("o");
                if (!_emittedStops.Add(key)) continue;

                var node = new JsonObject
                {
                    ["id"] = stop.Id,
                    ["startedAt"] = stop.StartedAt.ToString("o"),
                    ["endedAt"] = stop.EndedAt.Value.ToString("o"),
                };
                if (stop.Name != null) node["name"] = stop.Name;
                tx.AddRouteStop(node);
            }
        }

        private async Task EmitParticipantsAsync(PlayUpdate tx, RunSnapshot snap)
        {
            if (snap.Allies.Count == 0) return;

            var participants = new List<JsonObject>(snap.Allies.Count);
            foreach (var ally in snap.Allies)
            {
                var participant = new JsonObject { ["steam"] = ally.Steam };
                // GAP: Steam lookup takes a ulong but the participant id is a string
                // everywhere else; the consumer parses back and forth. (#6.)
                if (ulong.TryParse(ally.Steam, out var steamId))
                {
                    var did = await _client.Steam.LookupDidAsync(steamId).ConfigureAwait(false);
                    if (did != null) participant["atproto"] = did;
                }
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
