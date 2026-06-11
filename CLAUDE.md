# ror2.at — working notes

A **Risk of Rain 2 BepInEx plugin** (`mod/`) that publishes each run to the player's
atproto PDS as a `games.gamesgamesgamesgames.actor.play` record, via the
**`ByJP.AtprotoGaming.Core`** package in the sibling repo `../atproto-gaming-dotnet`.
Built both as a real mod and as the first consumer used to pressure-test that package
(all 7 API gaps found are fixed — see `docs/api-gaps.md`).

## 🚩 ACTIVE TASK: make the mod actually compile against the real game
The package side is solid (154 tests). The **RoR2-facing glue has never been compiled
against the game assemblies** — every RoR2 type reference is an assumption. JP is
shipping the game's managed assemblies so I can verify exact signatures.

**Next steps (resume here):**
1. Wait for `refs/` to contain the game DLLs — JP zips
   `…/steamapps/common/Risk of Rain 2/Risk of Rain 2_Data/Managed/` (+ optionally
   `MMHOOK_RoR2.dll`) and drops it at `ror2.at/refs/` (gitignored; not redistributable).
   He'll say "it's there."
2. Write a `MetadataLoadContext` reflection dumper (metadata-only, no Unity runtime,
   no game execution) — load `RoR2.dll` with a `PathAssemblyResolver` over the whole
   `Managed/` folder; print exact signatures for the checklist below.
3. Rewrite `mod/Ror2/StateExtractor.cs`, `mod/Ror2/RunTracker.cs`, `mod/Plugin.cs`
   against ground truth. Then JP builds on a real box (`dotnet build -t:Install`) and
   we iterate on remaining errors + watch a run. (SteamOS/Steam Deck is immutable & has
   no SDK, so introspect the DLLs on the Mac, don't build on the Deck.)

### RoR2 API surface to verify (every line in `mod/Ror2/*` is a candidate; key ones):
- **Events/hooks** (`RunTracker.Hook`): `Run.onRunStartGlobal`, `Stage.onStageStartGlobal`,
  `Run.onServerGameOver`, `Run.onClientGameOverGlobal`, `Run.onRunDestroyGlobal` (exact
  delegate shapes), and `On.RoR2.UserProfile.AddAchievement` (the `orig_AddAchievement`
  delegate — needs `MMHOOK_RoR2.dll`, or derive from `RoR2.dll`'s original method).
- **Run**: `seed` (ulong), `GetStartTimeUtc()`, `GetRunStopwatch()`, `stageClearCount`,
  `IsArtifactEnabled(ArtifactDef)`, `Run.instance`; `EclipseRun.eclipseLevel`;
  `GameEndingDef.cachedName`; `RunReport.gameEnding`; `ArtifactCatalog.artifactDefs` + `.cachedName`.
- **Player**: `LocalUserManager.GetFirstLocalUser()`; `LocalUser.cachedMaster`/`.userProfile`/
  `.currentNetworkUser`; `CharacterMaster.GetBody()`/`.bodyPrefab.name`;
  `CharacterBody.healthComponent.health`/`.level`/`.inventory`;
  `Inventory.itemAcquisitionOrder`/`.GetItemCount(ItemIndex)`; `ItemCatalog.GetItemDef`;
  `NetworkUser.masterController`/`.id.value`; `PlayerCharacterMasterController.instances`/`.master`/`.networkUser`.
- **Stats**: `PlayerStatsComponent.currentStats` (`StatSheet`);
  `StatSheet.GetStatValueULong(StatDef)` and the per-subfield overload
  `GetStatValueULong(StatDef, string)`; `StatDef.totalKills/totalDamageDealt/totalDamageTaken/
  goldCollected/totalItemsCollected/highestLevel`.
- **Achievements**: `AchievementManager.achievementIdentifiers` (count + ids);
  `UserProfile.HasAchievement(string)`.
- **Unity/BepInEx**: `Application.version`, `Time.unscaledTime`, `WaitForSeconds`,
  `BaseUnityPlugin`/`BepInPlugin`/`ManualLogSource`.

## Layout
- `mod/Mapping/` — **engine-free**, references only the package. `RunSnapshot` (plain DTO)
  + `PlayRecordMapper` (drives `OpenPlay`/`BeginUpdate`/`CommitAsync`/`Stats`/`Steam`).
  This is the layer that found the package gaps; it **compiles** (see verification).
- `mod/Ror2/` — **RoR2-coupled, VERIFY-flagged, never compiled**: `StateExtractor`
  (Run/StatSheet/inventory → snapshot) + `RunTracker` (lifecycle hooks + dirty-bit/throttle
  emit coroutine + achievement-count push).
- `mod/Plugin.cs` (BepInEx entry: wires 3 adapters, config, login on a Task, hooks),
  `mod/Adapters/BepInExLogSink.cs`, `mod/Config/Ror2PlayConfig.cs`.

## Verifying the package-facing code (do this after any Mapping/ change)
The mapping layer is compiled against the real package at `/tmp/mapcheck`:
`cd /tmp/mapcheck && dotnet build` (its `.csproj` `Compile Include`s `mod/Mapping/*.cs`
and `ProjectReference`s the package). **0 errors = the package-facing code is type-correct.**
The `mod/Ror2/*` glue is NOT in mapcheck (needs game DLLs).

## Build setup (`mod/ByJP.Ror2.Play.csproj`)
net472. Game/BepInEx/`MMHOOK_RoR2.dll` referenced in-place via HintPath into the install
(auto-detected per-OS; override `Ror2Dir` in `mod/local.props`). Package via local NuGet
feed (`mod/nuget.config` → `../packages`; `dotnet pack` the package there first).
`dotnet build -t:Install` deploys the plugin + its System.Text.Json deps. Optional
build-time signing: `/p:SigningPrivateKey=did:key:z…`. Needs BepInEx 5 + HookGenPatcher.

## Key design facts
- Writes the **shared** `actor.play` record (not the old bespoke `me.byjp.pesos.ror2.run`).
- rkey = `DerivePlayID(startTimeUtc, seed)` — same on every multiplayer peer.
- Mapper re-states full acquisitions (`SetAcquisitions`) + re-arrives/leaves every route
  stop each snapshot, keyed by ordinal `instanceId` → crash-safe, no per-emit bookkeeping.
- Achievements → `Stats.AchievementsUnlockedAsync(unlocked, total)` (counts), gated on a
  count change in `RunTracker` so the profile-load re-fire is skipped.
- Steam id passed as a string to `Steam.LookupDidAsync`.
