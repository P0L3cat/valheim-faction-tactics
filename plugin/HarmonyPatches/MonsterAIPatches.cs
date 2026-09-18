using System;
using FactionTactics.Orders;
using HarmonyLib;

#if VALHEIM_REFS
using UnityEngine;
#endif

namespace FactionTactics.HarmonyPatches
{
    /// <summary>
    /// Harmony entry + MonsterAI patch hypotheses.
    /// Patches are no-ops / stubs without VALHEIM_REFS so the solution stays coherent.
    /// </summary>
    public static class MonsterAIPatches
    {
        public static void Apply(Harmony harmony)
        {
#if VALHEIM_REFS
            harmony.PatchAll(typeof(MonsterAIPatches).Assembly);
            Plugin.Log.LogInfo("MonsterAI Harmony patches applied (validate TODOs in-game).");
#else
            // Compile-without-game-DLLs: nothing to patch.
            _ = harmony;
            Plugin.Log.LogInfo("VALHEIM_REFS not set — Harmony MonsterAI patches skipped (stubs mode).");
#endif
        }
    }

#if VALHEIM_REFS
    /// <summary>
    /// TODO hypotheses — method names must be verified against the local assembly_valheim.dll
    /// (dnSpy / ILSpy). Common Valheim MonsterAI touchpoints:
    ///   - UpdateAI(float dt)
    ///   - MoveTo / NavigateTo style movement
    ///   - SetTarget / GetAttackTarget / HuntTarget
    ///   - AvoidPlayer / Idle / Flee behaviors
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI))]
    public static class MonsterAI_UpdateAI_Patch
    {
        // TODO(hypothesis): postfix UpdateAI(float dt) after vanilla selects a target,
        // then steer toward MemberIntent.DesiredPosition when HoldGround / formation orders.
        [HarmonyPostfix]
        [HarmonyPatch("UpdateAI")]
        public static void UpdateAI_Postfix(MonsterAI __instance, float dt)
        {
            if (!TryResolveIntent(__instance, out var intent, out var id))
                return;

            if (intent.HoldGround)
            {
                // TODO(hypothesis): call __instance.MoveTo(dt, intent.DesiredPosition, 0.5f, false)
                // or StopMoving() when within slot tolerance.
                var ch = __instance.m_character;
                if (ch == null)
                    return;
                var pos = ch.transform.position;
                var dist = Vector3.Distance(pos, intent.DesiredPosition);
                if (dist > 1.25f)
                {
                    // __instance.MoveTo(dt, intent.DesiredPosition, 1f, intent.PreferRun);
                }
                else
                {
                    // __instance.StopMoving();
                }
            }

            _ = id;
            _ = dt;
        }

        // TODO(hypothesis): prefix/postfix on target selection to honor FocusTargetId
        // and ProtectMissiles (front line intercepts; missiles keep range).
        [HarmonyPrefix]
        [HarmonyPatch("SetAlerted")]
        public static void SetAlerted_Prefix(MonsterAI __instance, bool alert)
        {
            // Placeholder — confirm SetAlerted(bool) exists on this build.
            _ = __instance;
            _ = alert;
        }

        private static bool TryResolveIntent(MonsterAI ai, out MemberIntent intent, out long id)
        {
            intent = null!;
            id = 0;
            var ch = ai.m_character;
            if (ch == null)
                return false;
            id = ch.GetHashCode(); // TODO: stable ZDO id
            return OrderApplicator.TryGetIntent(id, out intent);
        }
    }

    /// <summary>
    /// Optional: BaseAI.MoveTo override steering.
    /// TODO(hypothesis): patch BaseAI.MoveTo(float, Vector3, float, bool) to redirect
    /// destination when MemberIntent is present and AllowVanillaChase is false.
    /// </summary>
    [HarmonyPatch]
    public static class BaseAI_MoveTo_Patch
    {
        // Intentionally not bound to a method until verified — avoids hard compile breaks
        // when signatures drift. Wire with HarmonyMethod + AccessTools in Apply() later.
    }
#endif
}
