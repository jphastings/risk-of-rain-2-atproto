using System;
using HarmonyLib;
using RoR2;

namespace ByJP.Ror2.Play.Ror2
{
    /// <summary>
    /// Harmony postfix on <see cref="UserProfile.AddAchievement"/>. Lets the mod react
    /// to achievement unlocks using only Harmony (which ships inside BepInEx), so the
    /// mod needs no HookGenPatcher / <c>MMHOOK_RoR2.dll</c> dependency. Raises
    /// <see cref="Added"/> after each unlock; the count recompute lives in RunTracker.
    /// </summary>
    [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.AddAchievement))]
    internal static class AchievementPatch
    {
        public static event Action? Added;

        [HarmonyPostfix]
        private static void Postfix() => Added?.Invoke();
    }
}
