# AT Protocol records

## Lexicon NSIDs

Two records, mirroring sts2.at:

- `me.byjp.pesos.ror2.run` — the per-run record. PUT repeatedly through the
  run; the latest PUT wins. Lexicon will live at
  `lexicons/me/byjp/pesos/ror2/run.json`.
- `games.gamesgamesgamesgames.actor.stats` — the rolling per-game playtime
  record from [Birbhouse's][birbhouse] HappyView ecosystem. One record per
  user per game, updated alongside each finished run. Lexicon is upstream;
  we just write to it.

The `me.byjp.pesos.<game>.run` namespacing scheme is jp's personal "play
event source of truth" (PESoS) family; reusing it here keeps the StS2 and
RoR2 records consistent for the eventual unified web viewer.

## Record rkey

A stable rkey per run, deterministic from `(seed, startTimeUtc)`. This
mirrors sts2.at's `Tid.FromRun(startTime, seed)` exactly:

- 53-bit microsecond timestamp (atproto TID prefix)
- 10-bit salt derived from `seed` low bits
- 2-bit version

Same rkey is used for every PUT through a run, so the latest snapshot replaces
the previous one. The web reader sees `getRecord(repo, ror2.run, rkey)`
return the most recent state without needing to enumerate.

### Why these two fields

- **`seed`** — `[SyncVar]` ulong, host-authoritative and replicated to
  every client. ProperSave also serialises it, so it survives a
  save-and-quit. Cannot be used alone: replayed seeds (manually entered
  via the lobby's seed entry UI, weekly/Prismatic Trial seeds, fixed-seed
  mods) collide.
- **`startTimeUtc`** — `[SyncVar]` `DateTime`, assigned once in
  `Run.Awake()` under `if (NetworkServer.active)` and replicated to
  clients. **Stable across pause/resume** (single assignment site;
  nothing in pause code touches it) and **identical on every peer in
  the lobby** (byte-identical ticks). **Not** preserved by ProperSave
  in its vanilla state — a save-and-quit followed by resume in a new
  launch would assign a fresh `DateTime.UtcNow`, *unless* we hook into
  ProperSave's save (see below).

The combination is collision-resistant within a session and across the
whole lobby. Vanilla RoR2 has no save-and-quit feature, so for a
ProperSave-free install the pair is fully stable for the entire
lifetime of a run.

For multiplayer: every peer writes its own record with **the same rkey**
to its own PDS. The web reader joins them by deriving the same rkey from
the host-broadcast `(seed, startTimeUtc)` pair, then `getRecord(allyDid,
ror2.run, rkey)` per ally.

### Bridging the ProperSave gap

[ProperSave][propersave] is a popular third-party mod that adds the
save-and-quit feature vanilla lacks. Its save format doesn't include
`startTimeUtc`, so without our intervention a resumed run would mint a
fresh value in `Run.Awake()` and our rkey would fork.

ProperSave publishes an event-based extension API for exactly this
case. When installed, we subscribe:

- **`ProperSave.SaveFile.OnGatherSaveData`** — fires at save time. We
  push our captured `startTimeUtc` (the one from the *original* run
  start) into the save's modded-data dictionary under a known key
  (`"me.byjp.pesos.ror2.startTimeUtc"`).
- **`ProperSave.Loading.OnLoadingStarted`** — fires when a save begins
  loading. We read the stored value via
  `CurrentSave.GetModdedData<DateTime>("me.byjp.pesos.ror2.startTimeUtc")`
  and use it in place of `Run.instance.GetStartTimeUtc()` when deriving
  the rkey for the resumed run.

This is a **soft** dependency: if ProperSave isn't installed, the
event subscriptions silently never fire and we use `Run.instance`
directly. If it is installed, the resumed run's rkey matches the
original run's. The seed is already in ProperSave's save (its own
serialisation), so we don't need to persist it ourselves.

In multiplayer with ProperSave: only the host serialises the run state,
so only the host's save carries our stored `startTimeUtc`. On resume,
the host computes the rkey from the restored value; clients receive the
restored `startTimeUtc` via the regular SyncVar replication at run
start; the lobby converges on the same rkey as before. (Verify in
testing — this depends on ProperSave restoring `_uniqueId`/`startTimeUtc`
SyncVars *before* the first replication tick. If it doesn't, host and
clients will diverge for one frame and we'll need to gate rkey
derivation on a stable signal.)

### Alternatives considered

1. **`seed` alone** — survives ProperSave but collides on replayed
   seeds (manual seed entry, weekly-seed mode). Rejected.
2. **`Run.GetUniqueId()` (a vanilla `[SyncVar] NetworkGuid`)** — single
   field, designed for run identity, lobby-consistent. Equivalent to
   `(seed, startTimeUtc)` for our purposes but not preserved by
   ProperSave either, so it'd need the same `OnGatherSaveData`
   treatment. Kept the tuple instead because `startTimeUtc` also
   serves the `startedAt` field in the record body — one fewer thing
   to extract.
3. **Local `seed → uniqueId` cache in our mod folder** — a previous
   draft of this doc. Broken: a deliberately replayed seed would hit
   the cache and overwrite the prior run's record.
4. **Mod-injected `[SyncVar] NetworkGuid` on a custom `NetworkBehaviour`**
   — bulletproof identity but modifies the game's network surface.
   Ruled out: prefer "extract and remember" over "extend the game."

[propersave]: https://github.com/KingEnderBrine/-RoR2-ProperSave

## Update model

Every snapshot is a `com.atproto.repo.putRecord` against the same rkey. There
is no "create then update" split — the first PUT creates and subsequent PUTs
replace. This is identical to sts2.at and is the easier mental model.

If the PUT fails with a network error (transient), the prepared JSON is
written to the on-disk outbox under `outbox/<encoded-did>/<rkey>.json`. On
reconnect — driven by either the next throttled emission, or the periodic
auth recheck — the outbox flushes. The currently-active run's queue file is
skipped during a flush so it doesn't race the live publisher.

If the PUT fails with a 4xx (record validation, auth failure), the record is
dropped with an error log. 4xx won't be fixed by retrying offline.

## Record shape (v0 sketch)

This is a planning sketch, not the final lexicon. See [`stats.md`](stats.md)
for what each field carries.

```jsonc
{
  "$type": "me.byjp.pesos.ror2.run",

  // Identity & outcome
  "outcome": "in_progress",            // | "victory" | "death" | "abandoned" | "obliterated"
  "gameEnding": null,                  // GameEndingDef.cachedName at game-over

  // Run metadata
  "mode": "Run",                       // "Run" | "InfiniteTowerRun" | "WeeklyRun" | "EclipseRun"
  "eclipseLevel": null,                // 1..8 on Eclipse only
  "difficulty": "DIFFICULTY_HARD_NAME",
  "seed": "9123847291203847",
  "startedAt": "2026-06-05T20:14:32.000Z",
  "endedAt": null,
  "stopwatchSeconds": 1283.4,
  "stageClearCount": 4,
  "loopClearCount": 0,
  "currentStage": "golemplains",
  "currentStageType": "Stage",
  "artifacts": ["Artifacts.Command", "Artifacts.Swarms"],
  "rules": { /* rest of ruleBook choices keyed by RuleDef.globalName */ },

  // Local player
  "steamID64": "76561197960265729",
  "userName": "jp",
  "bodyName": "HuntressBody",

  // Vanilla StatSheet readout
  "stats": {
    "totalKills": 412,
    "totalDamageDealt": 982134,
    "totalDamageTaken": 8431,
    "highestDamageDealt": 14982,
    "goldCollected": 5132,
    "totalItemsCollected": 31,
    "totalTimeAlive": 1280,
    "damageDealtAs": { "HuntressBody": 982134 },
    "killsAs":       { "HuntressBody": 412 }
  },

  // Inventory
  "items": { "Syringe": 8, "Crowbar": 2, "Hoof": 4 },
  "itemOrder": ["Syringe", "Hoof", "Crowbar", "Syringe", "Hoof", "..."],
  "equipment": "Jetpack",

  // Custom mini-trackers
  "stages": [
    { "scene": "blackbeach",  "seconds": 198 },
    { "scene": "golemplains", "seconds": 312 }
  ],
  "bosses": [
    { "stage": "golemplains", "name": "BeetleQueen2Body", "atSeconds": 510 }
  ],
  "pickups": [
    { "item": "Syringe", "atSeconds": 22 },
    { "item": "Hoof",    "atSeconds": 41 }
  ],

  // Achievements (per-peer; client-authoritative) — see achievements.md
  "achievementsHeld": [
    "CompleteThirtyStages",
    "AcquireAllEquipment"
  ],
  "achievementsUnlocked": [
    { "id": "BeatGame.HuntressBody", "atSeconds": 1832 }
  ],

  // Multiplayer
  "allies": [
    {
      "steam": "76561197994000231",
      "atproto": "did:plc:abcdef",
      "bodyName": "CommandoBody",
      "kills": 188,
      "deaths": 1
    }
  ],

  // Cross-record link
  "game": "at://did:web:gamesgamesgamesgames.games/games.gamesgamesgamesgames.game/<ror2-rkey>",
  "statsRef": "at://<userDid>/games.gamesgamesgamesgames.actor.stats/<rkey>",

  // Mod metadata
  "modVersion": "0.1.0",
  "gameVersion": "1.3.6",
  "updatedAt": "2026-06-05T20:35:55.123Z"
}
```

### Per-field optionality

The lexicon should mark **every field except `outcome`, `mode`, `seed`,
`startedAt`, `updatedAt` as optional**, matching sts2.at's "soft-breaking
schema" stance. Old records with the v0 shape need to keep rendering after
the lexicon evolves.

## Rolling stats record

After each *finished* run (not in-progress snapshots), the mod also updates
`games.gamesgamesgamesgames.actor.stats`:

```jsonc
{
  "$type": "games.gamesgamesgamesgames.actor.stats",
  "game": { "uri": "at://did:web:gamesgamesgamesgames.games/games.gamesgamesgamesgames.game/<ror2-rkey>" },
  "source": "steam",
  "playtime": 1283,            // cumulative minutes across all runs
  "lastPlayed": "2026-06-05T20:35:55.123Z",
  "createdAt": "2026-04-01T00:00:00.000Z"
}
```

Same logic as sts2.at's `EnsureStatsRecordAsync` / `MergeStatsDeltaAsync`. The
rkey for this record is cached in the mod's `config.json` (`statsRkey`) after
the first publish; subsequent runs PUT against the same rkey to increment
`playtime`.

The `at-uri` pointing to the canonical RoR2 `games.*.game` record is a
new value we'll register with the Birbhouse catalogue — it's how
HappyView-style apps know which game a stats record is for. Until we
register a real one, leave the field unset and the catalogue dialog will
just show "unknown game."

## Multiplayer & multi-PDS

A four-player Eclipse-8 run produces:

- 1 host's PDS: full record with `allies = [B, C, D]`.
- 3 client PDSes: full record each, `allies = [host + the other two]`.

Each peer's record is its own perspective. None of the four PDSes is canonical.
The web viewer is the place that joins them. The `(seed, startTimeUtc)`
deterministic rkey is the join key.

If a peer is offline at run end, their record goes to their local outbox and
publishes whenever they next have credentials + network. The other peers'
records on their own PDSes are unaffected.

## Signing

Same opt-in CID-first ECDSA P-256 inline attestation as sts2.at. Embedded at
build time via a GitHub Actions secret. Verifier looks up the `key` field in
a public `web/static/.well-known/ror2-mod-keys/keys.json` (path matches the
sts2 convention). Implementation lifts straight into `ror2.at.core/Signing/`.

[birbhouse]: https://birb.house/
