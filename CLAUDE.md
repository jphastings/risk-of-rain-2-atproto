# ror2.at — working notes

A **Risk of Rain 2 BepInEx plugin** (`mod/`) that publishes each run to the player's
atproto PDS as a `games.gamesgamesgamesgames.actor.play` record, via the
**`ByJP.AtprotoGaming.Core`** package in the sibling repo `../atproto-gaming-dotnet`.
Built both as a real mod and as the first consumer used to pressure-test that package
(all 7 API gaps found are fixed — see `docs/api-gaps.md`).

## ✅ Compiles against the real game (2026-06-11). Remaining: a live runtime playtest.
The whole mod now builds **0 warnings / 0 errors** against the shipped RoR2 assemblies +
BepInEx 5.4.23 core + the Core package. Every RoR2 type reference was checked against
`RoR2.dll` metadata (not guessed). What's left is a *runtime* test on a real box: JP builds,
deploys, and we watch a run produce a `.play` record.

**BepInEx is the mod's ONLY dependency** (see `manifest.json`). The one game patch we need
(achievement unlocks) is a **Harmony postfix** (`mod/Ror2/AchievementPatch.cs`) — Harmony
ships inside BepInEx — so there is **no HookGenPatcher / `MMHOOK_RoR2.dll` dependency**.
Distribute via Thunderstore; r2modman / the Thunderstore Mod Manager then auto-installs
BepInEx, making it a one-click install for players.

### How the Mac compile-verification is wired (reproduce any time)
- **Reflection dumper** (the source of truth for every signature):
  `/tmp/ror2dump` — a `MetadataLoadContext` over `refs/Managed/*.dll`. Commands:
  `types <T…>`, `member <T> <needle>`, `find <substr>`, `enum <T>`, `hook <T> <method>`
  (derives the MonoMod `orig_`/`hook_` delegate shape from the original method).
  Run: `cd /tmp/ror2dump && dotnet run -- /Users/jp/src/personal/ror2.at/refs/Managed types RoR2.Run`.
- **`refs/` layout** (gitignored, Mac-only, not redistributable):
  - `refs/Managed/` — the game's full `Managed/` folder (copied from the external drive;
    all 143 DLLs so base-type chains like `NetworkBehaviour` resolve).
  - `refs/install/` — a fake install tree `local.props` points `Ror2Dir` at:
    `Risk of Rain 2_Data/Managed` → symlink to `../../Managed`; `BepInEx/core/` (the
    downloaded BepInEx 5.4.23.2 core DLLs, incl. `0Harmony.dll`). (No MMHOOK needed any
    more — the achievement patch uses Harmony. A leftover `BepInEx/plugins/MMHOOK/`
    compile shim from the earlier MMHOOK approach may still sit here, now unreferenced.)
- **Reproduce the full build:** `dotnet pack ../atproto-gaming-dotnet/src/ByJP.AtprotoGaming.Core
  -c Release -o packages` (fills the local feed), then `cd mod && dotnet build`. `mod/local.props`
  (gitignored) sets `Ror2Dir=…/ror2.at/refs/install`.
- Steam Deck is immutable & SDK-less → introspect/build on the Mac, never on the Deck.

### Verified RoR2 surface — ground-truth signatures (corrected from the old guesses)
- **Events/hooks** (`RunTracker.Hook`): `Run.onRunStartGlobal` `Action<Run>`,
  `Stage.onStageStartGlobal` `Action<Stage>`, `Run.onServerGameOver` `Action<Run,GameEndingDef>`,
  `Run.onClientGameOverGlobal` `Action<Run,RunReport>`, `Run.onRunDestroyGlobal` `Action<Run>`.
  Achievements: a **Harmony postfix** on `UserProfile.AddAchievement(string, bool)`
  (`AchievementPatch`) raises an event; RunTracker recomputes the count. (Was an
  `On.RoR2` MMHOOK hook — switched to Harmony to drop the HookGenPatcher dependency.)
- **Run**: `seed` (ulong), `GetStartTimeUtc()`→`DateTime`, `GetRunStopwatch()`→`float`,
  `stageClearCount` (int), `Run.instance`, `ruleBook`, `selectedDifficulty`.
  ⚠️ **No `Run.IsArtifactEnabled`** → use `RunArtifactManager.instance.IsArtifactEnabled(ArtifactDef)`,
  iterating `ArtifactCatalog.GetArtifactDef((ArtifactIndex)i)` for `i in 0..artifactCount`
  (`ArtifactCatalog.artifactDefs` is **private**).
  ⚠️ **No `EclipseRun.eclipseLevel`** → `EclipseRun.GetEclipseLevelFromRuleBook(run.ruleBook)`.
  Outcome: `GameEndingDef.isWin` (cleaner than name-matching); `RunReport.gameEnding`.
- **Player**: `LocalUserManager.GetFirstLocalUser()`; `LocalUser.cachedMaster`/`.userProfile`/
  `.cachedStatsComponent` (use this for the StatSheet, not the networkUser→masterController walk);
  `CharacterMaster.GetBody()`/`.bodyPrefab.name`/`.inventory`;
  `CharacterBody.healthComponent.health` (float)/`.level` (**float**)/`.inventory`;
  `Inventory.itemAcquisitionOrder`/`.GetItemCountEffective(ItemIndex)` (`GetItemCount` is
  `[Obsolete]`); `ItemCatalog.GetItemDef`; `PlayerCharacterMasterController.instances`/`.master`/`.networkUser`.
- **Steam id**: `NetworkUser.id` is a `NetworkUserId` (struct) with a `steamId` getter →
  `RoR2.PlatformID`; guard `pid.isSteam`, emit `pid.ToSteamID()` (decimal SteamID64 string).
  (Old `id.value.ToString()` was wrong for non-steam users.)
- **Stats** (`RoR2.Stats`): `PlayerStatsComponent.currentStats` (`StatSheet`);
  `StatSheet.GetStatValueULong(StatDef)`; per-body overload is
  `GetStatValueULong(PerBodyStatDef, string bodyName)` (**`PerBodyStatDef`**, not `StatDef`) —
  use `PerBodyStatDef.damageDealtAs`/`killsAs`. `StatDef.totalKills/totalDamageDealt/
  totalDamageTaken/goldCollected/totalItemsCollected/highestLevel` confirmed.
- **Achievements** (`RoR2`): `AchievementManager.readOnlyAchievementIdentifiers`
  (`achievementIdentifiers` is **private**) + `UserProfile.HasAchievement(string)`.
- **Build refs**: the csproj needs **`com.unity.multiplayer-hlapi.Runtime`** (UNet's
  `NetworkBehaviour`, the base of Run/Stage/CharacterMaster/etc.) on top of RoR2 +
  UnityEngine + UnityEngine.CoreModule + BepInEx + 0Harmony. (No MMHOOK ref.)
- **Unity/BepInEx** (unchanged, stable): `Application.version`, `Time.unscaledTime`,
  `WaitForSeconds`, `BaseUnityPlugin`/`BepInPlugin`/`ManualLogSource`.

## Layout
- `mod/Mapping/` — **engine-free**, references only the package. `RunSnapshot` (plain DTO)
  + `PlayRecordMapper` (drives `OpenPlay`/`BeginUpdate`/`CommitAsync`/`Stats`/`Steam`).
  This is the layer that found the package gaps; it **compiles** (see verification).
- `mod/Ror2/` — **RoR2-coupled** (now compiled against the real game): `StateExtractor`
  (Run/StatSheet/inventory → snapshot), `RunTracker` (lifecycle hooks + dirty-bit/throttle
  emit coroutine + achievement-count push), `AchievementPatch` (Harmony postfix on
  `UserProfile.AddAchievement`).
- `mod/Plugin.cs` (BepInEx entry: wires 3 adapters, config, login on a Task, hooks),
  `mod/Adapters/BepInExLogSink.cs`, `mod/Config/Ror2PlayConfig.cs`.

## Verifying changes
- **Whole mod** (preferred, now that `refs/` exists): `cd mod && dotnet build` — compiles
  every file against the real game DLLs + BepInEx core (incl. 0Harmony) + package. See the
  reproduce-the-build steps above. This supersedes mapcheck.
- **Mapping layer only** (no game DLLs needed): `/tmp/mapcheck` `Compile Include`s
  `mod/Mapping/*.cs` and `ProjectReference`s the package. Handy on a machine without `refs/`.
- **Game-API only, fast** (`/tmp/ror2check`): compiles just `StateExtractor.cs` +
  `RunSnapshot.cs` against `refs/Managed/*.dll` (no BepInEx/package) — isolates RoR2 type errors.

## Build setup (`mod/ByJP.Ror2.Play.csproj`)
net472. Two compile paths (gated by `UseGameLibs`):
- **Default — local install** (verified build; what `-t:Install` deploys against): game +
  BepInEx assemblies referenced in-place via HintPath, install auto-detected per-OS
  (override `Ror2Dir` in `mod/local.props`).
- **`-p:UseGameLibs=true` — CI / no game install**: publicised, body-stripped assemblies
  from the **BepInEx NuGet feed** (`nuget.bepinex.dev`, added in `mod/nuget.config`):
  `RiskOfRain2.GameLibs` 1.4.1-r.0 (the package is **`RiskOfRain2.GameLibs`**, NOT
  `RoR2GameLibs` — that name is dead), `UnityEngine.Modules` 2021.3.33, `BepInEx.Core`
  5.4.21 — all `IncludeAssets=compile PrivateAssets=all` so nothing game/engine-derived
  ships. Verified to build 0/0 with no local game.

`ByJP.AtprotoGaming.Core` restores from **nuget.org** (the mod pins `Version="0.1.0"`).
To build against an unreleased core, `dotnet pack` it into `../packages`, bump the version
+ `PackageReference`, and re-add the local feed in `mod/nuget.config` (escape-hatch note is
there). `dotnet build -t:Install` deploys the plugin + its System.Text.Json deps. Optional
build-time signing: `/p:SigningPrivateKey=did:key:z…`. Needs **BepInEx 5 only** (Harmony
bundled; no HookGenPatcher).

## Releases (`.github/workflows/release.yml`)
Modelled on the sibling sts2.at release workflow. Triggers on a push to `main` that
changes `mod/ByJP.Ror2.Play.csproj` (i.e. a `<Version>` bump), skips if the `v<version>`
tag already exists, else: builds with `-p:UseGameLibs=true` (core restores from nuget.org,
GameLibs from the BepInEx feed — no game install, no sibling-repo checkout), zips a
`ByJP.Ror2.Play/` folder (plugin + deps + manifest + README), and `gh release create`s it.
Players download that zip from GitHub Releases (see the root README) or install via a mod
manager. Optional record-signing key is read from the `MOD_SIGNING_PRIVATE_KEY` secret.

After the GitHub Release it optionally publishes to **Thunderstore** (`riskofrain2`
community) via `GreenTF/upload-thunderstore-package` — **inert unless** the
`THUNDERSTORE_TEAM` variable + `THUNDERSTORE_TOKEN` secret are set (guarded by
`vars.THUNDERSTORE_TEAM != ''`). The action builds the Thunderstore manifest from workflow
inputs + the repo-root `icon.png` (256×256 — currently a placeholder) + root `README.md`;
the BepInEx dep is `bbepis-BepInExPack` (also pinned in `mod/manifest.json` for the
GitHub-zip / r2modman local-import path). The first Thunderstore publish is unverified
(can't dry-run the upload) — watch it and confirm the package installs + the plugin loads.

## Key design facts
- Writes the **shared** `actor.play` record (not the old bespoke `me.byjp.pesos.ror2.run`).
- rkey = `DerivePlayID(startTimeUtc, seed)` — same on every multiplayer peer.
- Mapper re-states full acquisitions (`SetAcquisitions`) + re-arrives/leaves every route
  stop each snapshot, keyed by ordinal `instanceId` → crash-safe, no per-emit bookkeeping.
- Achievements → `Stats.AchievementsUnlockedAsync(unlocked, total)` (counts), gated on a
  count change in `RunTracker` so the profile-load re-fire is skipped. Trigger is a Harmony
  postfix on `UserProfile.AddAchievement` (`AchievementPatch`), not an MMHOOK hook.
- Steam id passed as a string to `Steam.LookupDidAsync`.
- Config is BepInEx-backed (`BepInExConfigStore`): `[Login] Handle`/`AppPassword`,
  `[Recording] Source`/`ThrottleSeconds`, auto-managed `[Cache]`. Credentials are
  **re-validated live** — editing Handle/AppPassword fires `SettingChanged` →
  `CredentialsChanged` → `LoginAsync`, and a **read-only `[Login] Status`** line shows
  `✓ signed in as …` / `✗ rejected: …` (in-game ConfigurationManager + the log). BepInEx
  has no save-veto hook, so we validate-and-report rather than literally block a bad save.
  `ConfigurationManagerAttributes` (`ReadOnly`) is duck-typed by ConfigurationManager;
  harmless if that plugin is absent.
