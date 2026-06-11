using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Stats;
using ByJP.Ror2.Play.Mapping;

namespace ByJP.Ror2.Play.Ror2
{
    /// <summary>
    /// Reads the current <c>Run.instance</c> / player state into an engine-free
    /// <see cref="RunSnapshot"/>. Pure read; called on the main thread (RoR2 objects
    /// aren't thread-safe) before handing the snapshot to a background emit.
    /// </summary>
    /// <remarks>
    /// Every member here was verified against the shipped <c>RoR2.dll</c> metadata
    /// (see the dumper in the package repo's working notes). Identifier conventions
    /// follow docs/stats.md (stable internal names, not localised strings).
    /// </remarks>
    internal static class StateExtractor
    {
        public static RunSnapshot Capture(Run run)
        {
            var snap = new RunSnapshot
            {
                Seed = run.seed.ToString(),
                StartedAt = new DateTimeOffset(run.GetStartTimeUtc(), TimeSpan.Zero),
                Mode = run.GetType().Name,
                StopwatchSeconds = (int)run.GetRunStopwatch(),
                StageClearCount = run.stageClearCount,
            };

            snap.Difficulty = run is EclipseRun
                ? EclipseRun.GetEclipseLevelFromRuleBook(run.ruleBook)
                : (int)run.selectedDifficulty;

            CaptureArtifacts(snap);
            CaptureLocalPlayer(snap);
            CaptureAllies(snap);
            return snap;
        }

        private static void CaptureArtifacts(RunSnapshot snap)
        {
            var artifacts = RunArtifactManager.instance;
            if (artifacts == null) return;

            for (var i = 0; i < ArtifactCatalog.artifactCount; i++)
            {
                var def = ArtifactCatalog.GetArtifactDef((ArtifactIndex)i);
                if (def != null && artifacts.IsArtifactEnabled(def)) snap.Artifacts.Add(def.cachedName);
            }
        }

        private static void CaptureLocalPlayer(RunSnapshot snap)
        {
            var localUser = LocalUserManager.GetFirstLocalUser();
            var master = localUser?.cachedMaster;
            if (localUser == null || master == null) return;

            var bodyName = master.bodyPrefab != null ? master.bodyPrefab.name : null;
            var body = master.GetBody();
            if (body != null)
            {
                snap.Character = bodyName;
                if (body.healthComponent != null) snap.CurrentHp = (int)body.healthComponent.health;
                snap.CurrentLevel = (int)body.level;
                if (body.inventory != null) CaptureInventory(body.inventory, snap);
            }

            CaptureStats(localUser, bodyName, snap);
        }

        private static void CaptureInventory(Inventory inventory, RunSnapshot snap)
        {
            // itemAcquisitionOrder is chronological; the mapper re-states the whole
            // list each snapshot (deduped by instanceId), so re-reading it is fine.
            foreach (var itemIndex in inventory.itemAcquisitionOrder)
            {
                var def = ItemCatalog.GetItemDef(itemIndex);
                if (def == null) continue;
                snap.Items.Add(new ItemPickup
                {
                    Id = def.name,
                    Kind = "item",
                    Count = inventory.GetItemCountEffective(itemIndex),
                    AddedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        private static void CaptureStats(LocalUser localUser, string? bodyName, RunSnapshot snap)
        {
            var statsComponent = localUser.cachedStatsComponent;
            var sheet = statsComponent != null ? statsComponent.currentStats : null;
            if (sheet == null) return;

            Add(snap, sheet, "totalKills", StatDef.totalKills);
            Add(snap, sheet, "totalDamageDealt", StatDef.totalDamageDealt);
            Add(snap, sheet, "totalDamageTaken", StatDef.totalDamageTaken);
            Add(snap, sheet, "goldCollected", StatDef.goldCollected);
            Add(snap, sheet, "totalItemsCollected", StatDef.totalItemsCollected);
            Add(snap, sheet, "highestLevel", StatDef.highestLevel);

            // Body-keyed stats → nested maps the mapper emits as objects. Sparse:
            // only the current body, only non-zero values.
            if (bodyName != null)
            {
                AddPerBody(snap, sheet, "damageDealtAs", PerBodyStatDef.damageDealtAs, bodyName);
                AddPerBody(snap, sheet, "killsAs", PerBodyStatDef.killsAs, bodyName);
            }
        }

        private static void Add(RunSnapshot snap, StatSheet sheet, string key, StatDef def)
        {
            if (def == null) return;
            snap.Stats[key] = (long)sheet.GetStatValueULong(def);
        }

        private static void AddPerBody(RunSnapshot snap, StatSheet sheet, string mapKey, PerBodyStatDef def, string bodyName)
        {
            if (def == null) return;
            var value = (long)sheet.GetStatValueULong(def, bodyName);
            if (value == 0) return;
            if (!snap.StatMaps.TryGetValue(mapKey, out var map))
            {
                map = new Dictionary<string, long>();
                snap.StatMaps[mapKey] = map;
            }
            map[bodyName] = value;
        }

        /// <summary>
        /// The local player's account-wide RoR2 achievement progress (unlocked /
        /// total), for the stats record's <c>achievements</c> counts. Returns false
        /// if the profile isn't available yet.
        /// </summary>
        public static bool TryGetAchievementCounts(out int unlocked, out int total)
        {
            unlocked = 0;
            total = 0;
            var profile = LocalUserManager.GetFirstLocalUser()?.userProfile;
            if (profile == null) return false;

            var ids = AchievementManager.readOnlyAchievementIdentifiers;
            if (ids == null) return false;

            total = ids.Count;
            foreach (var id in ids)
                if (profile.HasAchievement(id)) unlocked++;
            return true;
        }

        private static void CaptureAllies(RunSnapshot snap)
        {
            var localMaster = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
            foreach (var controller in PlayerCharacterMasterController.instances)
            {
                if (controller == null || controller.master == localMaster) continue;
                var networkUser = controller.networkUser;
                if (networkUser == null) continue;

                // Only steam-backed users have a SteamID64 we can resolve to a DID;
                // EGS/EOS users have no steam id, so skip them.
                var steamId = networkUser.id.steamId;
                if (!steamId.isSteam) continue;
                snap.Allies.Add(new Ally { Steam = steamId.ToSteamID() });
            }
        }
    }
}
