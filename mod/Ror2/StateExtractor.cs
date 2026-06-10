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
    /// Identifier conventions follow docs/stats.md (stable internal names, not
    /// localised strings). Signatures marked <c>VERIFY</c> need a check against the
    /// installed game version's <c>MMHOOK_RoR2.dll</c> — field spellings drift.
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

            if (run is EclipseRun eclipse) snap.Difficulty = eclipse.eclipseLevel;

            CaptureArtifacts(run, snap);
            CaptureLocalPlayer(snap);
            CaptureAllies(snap);
            return snap;
        }

        private static void CaptureArtifacts(Run run, RunSnapshot snap)
        {
            // VERIFY: ruleBook walk for active artifact choices.
            foreach (var artifact in ArtifactCatalog.artifactDefs)
            {
                if (run.IsArtifactEnabled(artifact)) snap.Artifacts.Add(artifact.cachedName);
            }
        }

        private static void CaptureLocalPlayer(RunSnapshot snap)
        {
            var localUser = LocalUserManager.GetFirstLocalUser();
            var master = localUser?.cachedMaster;
            if (master == null) return;

            var body = master.GetBody();
            if (body != null)
            {
                snap.Character = master.bodyPrefab != null ? master.bodyPrefab.name : null;
                snap.CurrentHp = (int)body.healthComponent.health;
                if (body.inventory != null) CaptureInventory(body.inventory, snap);
            }

            var level = master.GetComponent<PlayerCharacterMasterController>();
            CaptureStats(localUser, snap);
        }

        private static void CaptureInventory(Inventory inventory, RunSnapshot snap)
        {
            // itemAcquisitionOrder is chronological; the mapper emits only the tail
            // it hasn't seen, so re-reading the whole list each snapshot is fine.
            var order = inventory.itemAcquisitionOrder;
            foreach (var itemIndex in order)
            {
                var def = ItemCatalog.GetItemDef(itemIndex);
                if (def == null) continue;
                snap.Items.Add(new ItemPickup
                {
                    Id = def.name,
                    Kind = "item",
                    Count = inventory.GetItemCount(itemIndex),
                    AddedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        private static void CaptureStats(LocalUser localUser, RunSnapshot snap)
        {
            var statsComponent = localUser.currentNetworkUser?.masterController?
                .GetComponent<PlayerStatsComponent>();
            var sheet = statsComponent?.currentStats;
            if (sheet == null) return;

            // A representative subset; docs/stats.md lists the full set. Enumerate
            // StatDef.allStatDefs at load and log it before trusting these names.
            Add(snap, sheet, "totalKills", StatDef.totalKills);
            Add(snap, sheet, "totalDamageDealt", StatDef.totalDamageDealt);
            Add(snap, sheet, "totalDamageTaken", StatDef.totalDamageTaken);
            Add(snap, sheet, "goldCollected", StatDef.goldCollected);
            Add(snap, sheet, "totalItemsCollected", StatDef.totalItemsCollected);
            Add(snap, sheet, "highestLevel", StatDef.highestLevel);
        }

        private static void Add(RunSnapshot snap, StatSheet sheet, string key, StatDef def)
        {
            if (def == null) return;
            snap.Stats[key] = (long)sheet.GetStatValueULong(def);
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
            var profile = LocalUserManager.GetFirstLocalUser()?.userProfile; // VERIFY: property name
            if (profile == null) return false;

            // VERIFY: AchievementManager surface — total defs + per-id unlocked check.
            var ids = AchievementManager.achievementIdentifiers;
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

                snap.Allies.Add(new Ally
                {
                    // id.value is a ulong; emit as a decimal string. EGS users have no Steam id.
                    Steam = networkUser.id.value.ToString(),
                    BodyName = controller.master?.bodyPrefab != null ? controller.master.bodyPrefab.name : null,
                });
            }
        }
    }
}
