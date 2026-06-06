# Stats

What each snapshot carries. Built primarily on RoR2's vanilla
`RoR2.Stats.StatSheet` so we lean on the game's accounting rather than
re-implementing counters. Things vanilla doesn't track get a custom
mini-tracker, but we keep that list small.

## Sources of truth

1. **`Run.instance`** — run-level metadata (seed, difficulty, mode, stopwatch,
   stage count, artifact rules).
2. **`PlayerCharacterMasterController.instances`** — every player in the lobby.
3. **`PlayerStatsComponent.currentStats`** (a `RoR2.Stats.StatSheet`) — the
   per-player accumulator that backs the in-game History UI.
4. **`CharacterMaster.inventory`** — per-player items, equipment, and the
   chronological pickup order.
5. **`RoR2.RunReport.Generate(Run, GameEndingDef)`** — only at game-over.
   Frozen snapshot equivalent to the History XML.
6. **Custom mini-trackers** — for the small set of things vanilla doesn't
   record (see [What we'll track ourselves](#what-well-track-ourselves)).

## Run-level fields

These go on every emission. All accessed via `Run.instance`.

| Field | Source | Type | Notes |
|---|---|---|---|
| `seed` | `Run.instance.seed` | `ulong` | Stable across `onRunStartGlobal` and save-loads. Serialise as string to dodge JSON 53-bit precision. |
| `mode` | `Run.instance.GetType().Name` | string | `"Run"` (classic), `"InfiniteTowerRun"` (Simulacrum), `"WeeklyRun"` (Prismatic), `"EclipseRun"`. |
| `eclipseLevel` | `(run as EclipseRun)?.eclipseLevel` | int? | 1–8 only on Eclipse. |
| `difficulty` | `DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty).nameToken` | string | The token (e.g. `"DIFFICULTY_HARD_NAME"`) is stable; store the token, not the localised name. |
| `stageClearCount` | `Run.instance.stageClearCount` | int | Stages cleared this loop. |
| `loopClearCount` | reflected (was `_loopClearCount` in ProperSave) | int | How many loops completed. |
| `stopwatchSeconds` | `Run.instance.GetRunStopwatch()` | float | In-game timer (excludes pauses). |
| `startedAt` | `Run.instance.GetStartTimeUtc()` | DateTime | Wall-clock. `[SyncVar]`, set host-only in `Run.Awake()` — every peer sees the same value. **Not** preserved by ProperSave save-and-quit; a resumed run gets a fresh `DateTime.UtcNow`. |
| `currentStage` | `Stage.instance.sceneDef.cachedName` | string | Stable id, e.g. `"golemplains"`. |
| `currentStageType` | `Stage.instance.sceneDef.sceneType` | enum-as-string | Stage / Intermission / etc. |
| `artifacts` | `run.ruleBook` walk | string[] | List of *active* artifact `RuleChoiceDef.globalName`s. |
| `rules` | `run.ruleBook` walk | object | All non-artifact rule choices keyed by `RuleDef.globalName`. |
| `outcome` | derived | string | `"in_progress"` / `"victory"` / `"death"` / `"abandoned"` / `"obliterated"`. |
| `gameEnding` | `GameEndingDef.cachedName` at game-over | string? | Granular: `"MainEnding"`, `"VoidEnding"`, `"ObliterationEnding"`, `"StandardLoss"`. |

## Per-player fields

A record carries the **local player's** stats as the top-level body. Other
peers go under `allies[]`. Identity:

| Field | Source | Notes |
|---|---|---|
| `steamID64` | `playerController.networkUser.id.value` | `ulong`; emit as decimal string. EGS users will be `null`. |
| `userName` | `networkUser.userName` | Display name. Don't rely on uniqueness. |
| `bodyName` | `master.bodyPrefab.name` | Stable internal id (e.g. `"CommandoBody"`). |
| `bodyToken` | `BodyCatalog.GetBodyName(idx)` | Token for localisation (e.g. `"COMMANDO_BODY_NAME"`). |
| `loadout` | `master.loadout` walk | Skill variants + skin index per body. Optional v0. |
| `kilroysFound` | not implemented in v0 | — |

### Stats from `StatSheet`

These read straight from `currentStats.GetStatValueULong(StatDef.X)`. The full
canonical list is exposed at runtime via `StatDef.allStatDefs` — **enumerate
it on plugin load and log it** before hardcoding a list, because field name
spellings have shifted between game versions (`goldCollected` vs.
`totalGoldCollected` is a known divergence).

Categorised picks for the v0 record:

**Progression / time**
- `totalTimeAlive` — seconds alive while stopwatch ran
- `totalStagesCompleted`, `highestStagesCompleted`
- `highestLevel` — highest survivor level

**Combat**
- `totalKills`, `totalMinionKills`
- `totalDeaths`
- `totalDamageDealt`, `totalMinionDamageDealt`
- `totalDamageTaken`, `totalHealthHealed`
- `highestDamageDealt` — max single-hit (vanilla tracks the max only)

**Economy**
- `goldCollected`, `maxGoldCollected`
- `totalDistanceTraveled`
- `totalItemsCollected`, `highestItemsCollected`
- `totalPurchases`
- `totalGoldPurchases`, `totalBloodPurchases`, `totalLunarPurchases`
- `totalTier1Purchases`, `totalTier2Purchases`, `totalTier3Purchases`
- `totalDronesPurchased`, `totalTurretsPurchased`
- `totalGreenSoupsPurchased`, `totalRedSoupsPurchased` (Scrappers/cauldrons)

**Achievements progress (unlockables)**
- `statSheet.unlockables` bitmask — emit as a list of `UnlockableDef.cachedName`.

### Per-body sub-fields

`StatSheet.fields` is sliced by `StatDef.fieldType`. For body-keyed stats
like `damageDealtAs` and `killsAs`, the lookup is
`currentStats.GetStatValueULong(StatDef.damageDealtAs, "CommandoBody")`.

For v0 we emit a flat map: `damageDealtAs: { "CommandoBody": 4321, "HuntressBody": 0 }`.
Sparse — only non-zero entries — and keyed by stable body name.

Same pattern for `killsAs.<BodyName>`, `damageDealtTo.<BodyName>`,
`killsAgainst.<BodyName>`.

## Inventory snapshot

Walked via `CharacterMaster.inventory`. Every emission:

| Field | Source | Notes |
|---|---|---|
| `items` | `inventory.itemStacks` × `ItemCatalog` | Map of `{ItemDef.name → count}`. Stable internal name as key. |
| `itemOrder` | `inventory.itemAcquisitionOrder` | Chronological list of `ItemDef.name`. Lets a viewer animate the run. |
| `equipment` | `inventory.currentEquipmentIndex` → `EquipmentCatalog` | Stable `EquipmentDef.name`, or `null` if none. |
| `voidItems` | walked from `items` against `ItemTierCatalog` | Convenience; web can derive. Maybe omit in v0. |

## Multiplayer allies

A list of every *other* player in the lobby:

```json
"allies": [
  { "steam": "76561197960265729" },
  { "steam": "76561197994000231", "atproto": "did:plc:..." }
]
```

Same shape as the sts2.at record. Backfill of `atproto` happens lazily —
we resolve Steam IDs to DIDs via the same Slingshot endpoint sts2.at uses
(see `SteamDidResolver.cs` in sts2.at) and PUT updated records.

For each ally we also include a slim per-player block — body, items, kills,
deaths — so the web view can render the whole team off one record. This is
the diff from sts2.at, which only records `(steam, atproto?)`.

## What we'll track ourselves

Vanilla doesn't track these and we want them:

- **Per-stage timings.** Snapshot `Run.instance.GetRunStopwatch()` on each
  `Stage.onStageStartGlobal` and emit deltas. Keyed by stage cached name.
- **Boss kill list.** Vanilla has no per-boss breakdown. Subscribe to
  `BossGroup.onBossGroupDefeatedServer` and append `{ stage, bossName,
  atSeconds }` to a `bosses[]` array.
- **Item pickup timing.** `Inventory.onInventoryChanged` debounced; on each
  net-positive delta append `{ item, count, atSeconds }` to `pickups[]`. Lets
  the web view show "items per minute" without parsing per-snapshot diffs.
- **Damage histogram (optional, off by default).** Hook
  `HealthComponent.TakeDamage` IL; bucket by power-of-two. Off by default
  because it's the heaviest hook and we likely don't need it for v0.

## Achievements

Captured via a separate hook surface (see [`achievements.md`](achievements.md)).
Two fields land in every emission:

- `achievementsHeld` — sparse list of `AchievementDef.identifier` strings
  the player had unlocked *before this run started*. Captured once at
  `Run.onRunStartGlobal` and held in memory.
- `achievementsUnlocked` — list of `{ id, atSeconds }` for unlocks
  granted *during* this run. Appended as `On.RoR2.UserProfile.AddAchievement`
  fires (filtered to `isExternal == true` so profile-load doesn't fake
  unlocks). Cleared on `Run.onRunStartGlobal`.

We deliberately don't store per-achievement progress for incomplete ones
— the raw counters that drive progress are already in
`PlayerStatsComponent.currentStats` (`totalStagesCompleted`, `totalKills`,
per-body `killsAs.<BodyName>`, etc.) and the web reader can compute
progress bars off those plus a static catalogue.

Each of these is implemented as its own small `IRunMiniTracker` class with a
`Reset()`, `Snapshot()` shape. They live in `mod/MiniTrackers/` and the
top-level extractor composes them.

## Catalogue identifier conventions

We store **stable internal names**, not localised strings or numeric indexes.
This is the same pattern the History XML uses:

| Catalogue | Identifier we store |
|---|---|
| `ItemCatalog` | `ItemDef.name` (e.g. `"SoulboundCatalyst"`) |
| `EquipmentCatalog` | `EquipmentDef.name` |
| `BodyCatalog` | bodyName (e.g. `"CommandoBody"`) |
| `SceneCatalog` | `sceneDef.cachedName` (e.g. `"golemplains"`) |
| `ArtifactCatalog` | `ArtifactDef.cachedName` |
| `DifficultyCatalog` | `DifficultyDef.nameToken` (token, not localised) |
| `GameEndingCatalog` | `GameEndingDef.cachedName` |
| `UnlockableCatalog` | `UnlockableDef.cachedName` |
| Mod-added content | same as vanilla — stable name field |

The web reader maps these to localised display strings using game JSON dumps,
identical to sts2.at's `web/static/names.json` pipeline.

## Size budget

A single snapshot:

- Run-level metadata: ~30 fields, ~1 KB.
- Per-player `stats`: ~30 numeric fields + sparse per-body maps. ~2 KB.
- Inventory: depends on item count; mid-run 30 items ≈ 1 KB.
- Allies (3 peers, slim): ~3 KB.
- Custom trackers: ~1 KB.

Target: under 10 KB per snapshot. A typical 30-minute run with one snapshot
per minute ≈ 30 PUTs × 10 KB = 300 KB / run. PDSes are generous about
record count and we're putting the same rkey each time, so storage cost on
the user's PDS stays at the size of the *latest* snapshot.

## Sister-mod precedent worth mining

- **StatsPlus** ([source](https://github.com/mwoiii/stat-mod-ror2)) — already
  implements per-stage timing, shrine-purchase tracking, fall damage,
  last-standing tracking. Their `customStats/` folder is a ready-made
  reference for the [What we'll track ourselves](#what-well-track-ourselves)
  list.
- **ProperSave** ([source](https://github.com/KingEnderBrine/-RoR2-ProperSave)) —
  the canonical reference for serialising run state. `RunData.cs` and
  `PlayerData.cs` show exactly which `Run.instance` fields are worth
  capturing. We don't re-use the format — ProperSave saves to disk for
  resume, we publish JSON to a PDS — but the field selection overlaps a lot.
