#if VALHEIM_REFS
using FactionTactics.Combat;
using FactionTactics.Orders;
using FactionTactics.Util;
using HarmonyLib;
using UnityEngine;

namespace FactionTactics.Client
{
    /// <summary>
    /// Owning-client executor (1.0.4): prefer RPC intent cache, then ZDO fallback.
    /// Drive combat while keeping local IsOwner (physics + hits stay low-latency).
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI))]
    public static class ClientMonsterAI_UpdateAI_Patch
    {
        public static long UpdateHits { get; private set; }
        public static long DriveHits { get; private set; }
        public static long SchemaRejects { get; private set; }
        public static long RpcDriveHits { get; private set; }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MonsterAI.UpdateAI))]
        public static bool Prefix(MonsterAI __instance, float dt)
        {
            UpdateHits++;

            if (ZNet.instance != null && ZNet.instance.IsDedicated())
                return true;

            if (!IntentZdoSync.IsNetOwner(__instance))
                return true;

            var now = Time.time;
            MemberIntent intent;
            var fromRpc = IntentRpcSync.TryGetFromMonsterAI(__instance, now, out intent);
            if (!fromRpc && !IntentZdoSync.TryReadFromMonsterAI(__instance, now, out intent))
            {
                // Schema mismatch only when ZDO has a non-zero foreign version (RPC miss + ZDO miss).
                if (IntentZdoSync.SchemaMismatches > SchemaRejects)
                    SchemaRejects = IntentZdoSync.SchemaMismatches;
                return true; // no FT intent → vanilla
            }

            // 1.0.12: Attack → release vanilla UpdateAI (native chase/swings). Formation stays FT.
            if (CombatAuthority.ShouldReleaseToVanillaUpdateAI(intent))
                return true;

            CombatDriver.Drive(__instance, intent, dt);
            DriveHits++;
            if (fromRpc)
                RpcDriveHits++;
            return false; // sole brain on owner (maneuver / formation)
        }
    }

    /// <summary>Belt-and-suspenders: HoldGround / slot rewrite for other MoveTo callers.</summary>
    [HarmonyPatch(typeof(BaseAI), "MoveTo")]
    public static class ClientBaseAI_MoveTo_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseAI __instance, float dt, ref Vector3 point, float dist, bool run)
        {
            if (!(__instance is MonsterAI mai))
                return true;
            if (!IntentZdoSync.IsNetOwner(mai))
                return true;
            var now = Time.time;
            if (!IntentRpcSync.TryGetFromMonsterAI(mai, now, out var intent)
                && !IntentZdoSync.TryReadFromMonsterAI(mai, now, out intent))
                return true;
            if (CombatAuthority.ShouldAllowVanillaMoveTo(intent))
                return true;
            if (intent.HoldGround)
            {
                __instance.StopMoving();
                return false;
            }
            if (intent.DesiredPosition != Vector3.zero &&
                (intent.PreferKeepRange || CombatDriver.IsLineFront(intent)))
            {
                point = intent.DesiredPosition;
            }
            return true;
        }
    }

    /// <summary>Register FT_MemberIntents when ZNet/ZRoutedRpc comes up (1.0.4: also join/update).</summary>
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static class ClientZNet_Awake_RegisterIntentRpc_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IntentRpcSync.EnsureRegistered();
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    public static class ClientZNet_OnNewConnection_RegisterIntentRpc_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IntentRpcSync.EnsureRegistered();
        }
    }

    [HarmonyPatch(typeof(ZNet), "Update")]
    public static class ClientZNet_Update_RegisterIntentRpc_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!IntentRpcSync.IsRegistered)
                IntentRpcSync.EnsureRegistered();
        }
    }

    [HarmonyPatch(typeof(Game), "Update")]
    public static class ClientGame_Update_RegisterIntentRpc_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!IntentRpcSync.IsRegistered)
                IntentRpcSync.EnsureRegistered();
        }
    }
}
#endif
