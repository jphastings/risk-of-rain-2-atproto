using System;
using System.Collections.Generic;

namespace ByJP.Ror2.Play.Mapping
{
    /// <summary>
    /// A plain, engine-free snapshot of a Risk of Rain 2 run. The RoR2 extractor
    /// fills this in from <c>Run.instance</c> / <c>StatSheet</c> / inventory; the
    /// <see cref="PlayRecordMapper"/> turns it into atproto play-record changes.
    /// Keeping it free of any RoR2 / Unity type is what lets the mapping layer be
    /// built and reasoned about against the package alone.
    /// </summary>
    public sealed class RunSnapshot
    {
        // ── identity (stable for the whole run, drives the play id) ──────────
        public string Seed { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }

        // ── settings, set before / at the start of the run ──────────────────
        public string Mode { get; set; } = "Run";            // Run / EclipseRun / WeeklyRun / InfiniteTowerRun
        public int? Difficulty { get; set; }                  // eclipse level, or a difficulty rank
        public string? Character { get; set; }                // bodyName, e.g. "HuntressBody"
        public List<string> Artifacts { get; } = new List<string>();

        // ── progress, refreshed every snapshot (absolute values) ────────────
        public int StopwatchSeconds { get; set; }
        public int StageClearCount { get; set; }
        public Dictionary<string, long> Stats { get; } = new Dictionary<string, long>();
        // Body-keyed stats (e.g. damageDealtAs, killsAs) → emitted as nested objects.
        public Dictionary<string, Dictionary<string, long>> StatMaps { get; } =
            new Dictionary<string, Dictionary<string, long>>();
        public int? CurrentHp { get; set; }
        public int? CurrentLevel { get; set; }

        // ── chronological lists (the mapper only emits the new tail) ─────────
        public List<ItemPickup> Items { get; } = new List<ItemPickup>();
        public List<StageVisit> Stages { get; } = new List<StageVisit>();

        // ── multiplayer (the full current set each snapshot) ────────────────
        public List<Ally> Allies { get; } = new List<Ally>();

        // ── terminal (set only at game over) ────────────────────────────────
        public RunOutcome? Outcome { get; set; }
        public string? OutcomeCause { get; set; }             // GameEndingDef.cachedName
        public DateTimeOffset? EndedAt { get; set; }
    }

    public enum RunOutcome { Succeeded, Failed, Abandoned }

    public sealed class ItemPickup
    {
        public string Id { get; set; } = "";                  // ItemDef.name, e.g. "Syringe"
        public string? Kind { get; set; }                     // "item" | "equipment"
        public string? Name { get; set; }                     // localised display name
        public int Count { get; set; }                        // stack size after this pickup
        public DateTimeOffset AddedAt { get; set; }
    }

    public sealed class StageVisit
    {
        public string Id { get; set; } = "";                  // sceneDef.cachedName, e.g. "golemplains"
        public string? Name { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
    }

    public sealed class Ally
    {
        public string Steam { get; set; } = "";               // SteamID64 as decimal string
        public string? BodyName { get; set; }
        public long Kills { get; set; }
        public long Deaths { get; set; }
    }
}
