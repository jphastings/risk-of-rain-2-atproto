# Achievements

How the mod listens for achievement unlocks and captures lifetime
achievement progress for each emission.

## Why bother

Achievements in RoR2 unlock characters, skill variants, and skins
("Mastery" skins etc.), so they're milestones a viewer cares about
seeing on a run timeline. They're also fundamentally **per-player**:
in a multiplayer run, four people can all be hunting different
character unlocks simultaneously, and each peer's PDS record should
reflect *their* unlocks, not the host's.

## Hook target

There's one stable hook that fires once per genuine unlock:

```csharp
On.RoR2.UserProfile.AddAchievement += (orig, self, id, isExternal) => {
    orig(self, id, isExternal);
    // self.HasAchievement(id) is now true
    // id is the stable AchievementDef.identifier (e.g. "CompleteThirtyStages")
};
```

Why this one (and not the alternatives):

- **`UserProfile.AddAchievement(string id, bool isExternal)`** — fires
  exactly once per real unlock, stable across the pre-SotS and post-SotS
  decompiles. `isExternal` is `true` when the grant comes from the
  in-game tracker (i.e. the player actually earned it this session).
- `BaseAchievement.OnGranted` — fires too, but mod-added achievement
  subclasses can override without calling `base`, so this hook silently
  misses modded unlocks.
- `UserProfile.onUnlockableGranted` — vanilla static event, but it only
  fires for achievements that have a non-empty
  `unlockableRewardIdentifier` (SotS added a few lunar-coin-only
  achievements with no unlockable). Also noisier: `UserAchievementManager.OnInstall`
  re-fires it on every game launch for achievements where the unlockable
  got de-synced from the profile.
- `On.RoR2.UI.AchievementNotificationPanel.*` — wrong layer: the panel
  suppresses display on the game-over screen, so we'd miss unlocks that
  happen mid-victory cutscene.

`AddAchievement` is the cleanest single point.

## Authority

**Client-authoritative**. The local `BaseAchievement` tracker runs on the
local client; `Grant()` writes to the local `UserProfile` and only then
sends a kill-feed notification to the host via
`NetworkUser.CallCmdReportAchievement`. A multiplayer client can unlock
an achievement based on their own actions without the host's permission.

Implication for our mod: **every peer needs the mod installed** to capture
their own achievement unlocks. Achievements unlocked by other peers in the
lobby are not visible to us — and they shouldn't be, because they're
posting to their own PDS.

Exception: `BaseServerAchievement` / `ServerAchievementTracker` are
server-tracked, but the host still calls `Grant()` on each eligible
client's `BaseAchievement`, so the terminal hook (`AddAchievement`) is
still client-side. Same hook works.

## What to put in a record

Two distinct fields, distinct purposes:

### `achievementsUnlocked` — granted during this run

A list, appended to as `AddAchievement` fires while a run is in progress.
Cleared on `Run.onRunStartGlobal`.

```json
"achievementsUnlocked": [
  { "id": "CompleteThirtyStages",       "atSeconds": 1832 },
  { "id": "BeatGame.RailgunnerBody",    "atSeconds": 2104 }
]
```

This is the high-signal one. A viewer can render an icon timeline.

### `achievementsHeld` — lifetime snapshot at run start

The list of achievement identifiers the player had unlocked *before*
this run began. Captured once at `Run.onRunStartGlobal` and frozen on
the in-memory run state.

```json
"achievementsHeld": [
  "CompleteThirtyStages",
  "TotalKills.1000",
  "AcquireAllEquipment"
]
```

Sparse — only held ones — to keep payload size sane. With every DLC the
total count is ~190+ as of late 2025; an empty-progress player's record
saves the bytes.

### Computed on the web reader

- Newly-unlocked-ever (`achievementsUnlocked - achievementsHeld`) vs.
  re-triggered (the empty intersection) — useful to distinguish "first
  time getting this!" from "the tracker fired again."
- Achievement progress bar (e.g. "210 / 240 held") — derive from
  `achievementsHeld.length` and a static count published with the
  lexicon.

We do **not** store per-achievement progress for incomplete ones (e.g.
"kill 850 / 1000 lemurians"). The vanilla `BaseAchievement.ProgressForAchievement()`
method exists but:

- The base implementation returns 0; only specific subclasses override it.
- Body-locked trackers return their progress only while the matching
  character is the local player's selection — misleading mid-run.
- The raw counters that *drive* progress already live in
  `PlayerStatsComponent.currentStats` (`totalStagesCompleted`,
  `totalKills`, per-body `killsAs.<BodyName>`, etc.) and we're already
  emitting those. The web reader can compute progress from those plus a
  static "what does each achievement need" map.

## Reading at run start

```csharp
var profile = LocalUserManager.GetFirstLocalUser().userProfile;
var held = new List<string>();
foreach (var def in AchievementManager.allAchievementDefs) {
    if (profile.HasAchievement(def.identifier))
        held.Add(def.identifier);
}
```

`AchievementManager` is static (there is no `AchievementCatalog` class
despite the naming convention used elsewhere in `RoR2.dll`). Init runs
under `[SystemInitializer(typeof(UnlockableCatalog))]`, so it's
guaranteed populated by the time `Run.onRunStartGlobal` fires.

## Identifier conventions

Stable PascalCase strings — language-independent, suitable for storing
in the record verbatim:

- `"CompleteThirtyStages"`
- `"KillBossQuick"`
- `"BeatGame.RailgunnerBody"`
- `"ToolbotKillImpBossWithBfg"`

For each identifier you can resolve:

| Want | API |
|---|---|
| Display name token | `def.nameToken` → `Language.GetString(token)` |
| Description token | `def.descriptionToken` |
| Icon path | `def.iconPath` (e.g. `"Textures/AchievementIcons/texCompleteThirtyStagesIcon"`) |
| Unlocked reward | `UnlockableCatalog.GetUnlockableDef(def.unlockableRewardIdentifier)` |
| Reverse lookup from an unlockable | `AchievementManager.GetAchievementDefFromUnlockable(unlockableId)` |
| Hidden? | derive: `def.prerequisiteAchievementIdentifier != null && !profile.HasAchievement(prereq)` |

Mod-added achievements (via `[RegisterAchievement]` or the legacy
`R2API.Unlockable.UnlockableAPI`) flow through the same
`AddAchievement` path with the mod's chosen string identifier. They'll
be captured by the same hook without special handling. Good.

DLC discrimination: there's no `DLC` field on `AchievementDef`. The
closest proxy is inspecting `unlockableRewardIdentifier` prefixes
(e.g. `"Skills.Heretic.*"` is SotV-era) or tracker namespaces. We don't
need to discriminate for the record — just store the identifier and
let the web reader cross-reference a static "this id is from
DLC=Survivors of the Void" map.

## Gotchas

1. **`UserProfile.OnLogin` re-pushes every stored achievement to the
   platform layer** (`PlatformSystems.achievementSystem.AddAchievement(name)`)
   so Steam stays in sync. This does **not** re-trigger `UserProfile.AddAchievement`,
   so our hook stays quiet — but be aware if you ever hook the platform
   layer directly, that path is noisy by N achievements per launch.
2. **`Grant()` is one frame deferred.** `BaseAchievement.Grant()` only
   sets `shouldGrant = true`; the actual `UserProfile.AddAchievement`
   call happens in the next `UserAchievementManager.Update()`. Our
   `On.UserProfile.AddAchievement` hook runs after that tick, so we're
   fine — but if we ever consider hooking `Grant()` directly, we'd be
   one frame ahead of the persisted profile state.
3. **`isExternal` flag matters.** When `AddAchievement(id, isExternal: false)`
   fires, the achievement is being restored from disk (profile-load),
   not actually unlocked. Filter `achievementsUnlocked` to
   `isExternal == true` to avoid recording the entire profile every
   game launch.
4. **No central "achievement granted" static event.** If a future RoR2
   patch adds one (e.g. `UserProfile.onAchievementGranted`), we can
   prefer it over the MonoMod hook. Until then, MonoMod is the way.
5. **Profile-resolution timing.** `LocalUserManager.GetFirstLocalUser()`
   can return null very early in boot. Call it inside
   `Run.onRunStartGlobal` or later — not in the plugin's `Awake()`.
6. **Mod-added achievements with no `nameToken`.** Some hastily-written
   mod achievements register with a missing or non-existent token. Be
   defensive when serialising — log the identifier even if we can't
   resolve a display name.

## Mapping the catalogue

The web reader needs to know what each identifier *means* (icon,
display name, what it unlocks). Sister project sts2.at handles this by
generating `web/static/names.json` from the game's localisation files.
We'll do the same for RoR2:

- Periodic dump of all `AchievementDef`s (identifier, nameToken,
  descriptionToken, iconPath, unlockableRewardIdentifier).
- Resolve tokens via the game's `Language` system to English.
- Commit the dump as `web/static/achievements.json` (and `unlockables.json`
  for the targets).

Likely implementation: a small one-shot debug command in the mod
(`Console.cmd: dump_achievements`) that writes the resolved catalogue
to disk on demand. Run once per game update.

## Sources

- [`RoR2.AchievementManager` (pre-SotS decompile)](https://github.com/DaveAldon/RiskOfRain2-Open-Source/blob/master/Assembly-CSharp/RoR2/AchievementManager.cs)
- [`RoR2.UserAchievementManager`](https://github.com/DaveAldon/RiskOfRain2-Open-Source/blob/master/Assembly-CSharp/RoR2/UserAchievementManager.cs)
- [`RoR2.Achievements.BaseAchievement`](https://github.com/DaveAldon/RiskOfRain2-Open-Source/blob/master/Assembly-CSharp/RoR2/Achievements/BaseAchievement.cs)
- [`RoR2.UserProfile` (post-SotS path with `PlatformSystems`)](https://github.com/WarmBuns/PoP2/blob/master/RoR2/UserProfile.cs)
- [`RoR2.AchievementDef`](https://github.com/WarmBuns/PoP2/blob/master/RoR2/AchievementDef.cs)
- [`R2API.Unlockable`](https://github.com/risk-of-thunder/R2API/blob/master/R2API.Unlockable/README.md) — for how mod-added achievements register
- [prodzpod/AchievementPins](https://github.com/prodzpod/AchievementPins) — reference for enumeration timing
- [dakkhuza/AchivementLoader](https://github.com/dakkhuza/AchivementLoader) — reference for `[RegisterAchievement]` use
