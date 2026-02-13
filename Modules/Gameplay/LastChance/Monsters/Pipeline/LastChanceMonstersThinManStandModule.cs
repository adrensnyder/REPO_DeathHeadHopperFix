#nullable enable

using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch(typeof(EnemyThinMan), "StateStand")]
    internal static class LastChanceMonstersThinManStandModule
    {
        private static readonly FieldInfo? s_playerTargetField = AccessTools.Field(typeof(EnemyThinMan), "playerTarget");
        private static readonly FieldInfo? s_enemyField = AccessTools.Field(typeof(EnemyThinMan), "enemy");
        private static readonly FieldInfo? s_enemyOnScreenField =
            AccessTools.Field(typeof(Enemy), "OnScreen") ??
            AccessTools.Field(typeof(Enemy), "onScreen");
        private static readonly FieldInfo? s_debugNoVisionField =
            AccessTools.Field(typeof(EnemyDirector), "debugNoVision");
        private static readonly MethodInfo? s_setTargetMethod = AccessTools.Method(typeof(EnemyThinMan), "SetTarget");
        private static readonly MethodInfo? s_updateStateMethod = AccessTools.Method(typeof(EnemyThinMan), "UpdateState");
        private static readonly System.Collections.Generic.Dictionary<int, int> s_lastTransitionFrameByEnemy = new();

        [HarmonyPostfix]
        private static void Postfix(EnemyThinMan __instance)
        {
            if (__instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            if (EnemyDirector.instance != null &&
                s_debugNoVisionField != null &&
                s_debugNoVisionField.GetValue(EnemyDirector.instance) is bool debugNoVision &&
                debugNoVision)
            {
                return;
            }

            if (s_playerTargetField == null || s_enemyField == null || s_setTargetMethod == null || s_updateStateMethod == null)
            {
                return;
            }

            if (s_playerTargetField.GetValue(__instance) is PlayerAvatar)
            {
                return;
            }

            var enemy = s_enemyField.GetValue(__instance) as Enemy;
            var onScreen = s_enemyOnScreenField?.GetValue(enemy) as EnemyOnScreen;
            if (onScreen == null)
            {
                return;
            }

            var players = GameDirector.instance?.PlayerList;
            if (players == null)
            {
                return;
            }

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null)
                {
                    continue;
                }

                // Vanilla requires !isDisabled; in LastChance, consider active head proxy as a valid "alive for ThinMan" target.
                var eligible = !LastChanceMonstersTargetProxyHelper.IsDisabled(player) || LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
                if (!eligible || !onScreen.GetOnScreen(player))
                {
                    continue;
                }

                s_setTargetMethod.Invoke(__instance, new object[] { player });
                var onScreenState = ResolveOnScreenStateValue();
                if (onScreenState != null)
                {
                    s_updateStateMethod.Invoke(__instance, new[] { onScreenState });
                }

                if (InternalDebugFlags.DebugLastChanceThinManFlow)
                {
                    var enemyId = __instance.GetInstanceID();
                    var frame = UnityEngine.Time.frameCount;
                    if (!s_lastTransitionFrameByEnemy.TryGetValue(enemyId, out var lastFrame) || frame - lastFrame >= 120)
                    {
                        s_lastTransitionFrameByEnemy[enemyId] = frame;
                        LastChanceMonstersOnScreenCameraModule.DebugLog(
                            "StateStand.ProxyAcquire",
                            $"enemy={__instance.gameObject.name} player={(player.photonView != null ? player.photonView.ViewID : -1)} fromDisabled={LastChanceMonstersTargetProxyHelper.IsDisabled(player)} headProxy={LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player)}");
                    }
                }

                return;
            }
        }

        private static object? ResolveOnScreenStateValue()
        {
            var enumType = AccessTools.Inner(typeof(EnemyThinMan), "State");
            if (enumType == null || !enumType.IsEnum)
            {
                return null;
            }

            try
            {
                return System.Enum.Parse(enumType, "OnScreen");
            }
            catch
            {
                return null;
            }
        }
    }
}
