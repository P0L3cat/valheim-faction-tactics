#if VALHEIM_REFS
using FactionTactics.Combat;
using FactionTactics.Orders;
using FactionTactics.Util;
using HarmonyLib;
using UnityEngine;

namespace FactionTactics.Client
{
    /// <summary>
    /// Owning-client executor: read ZDO intents from the server commander and Drive combat
    /// while keeping local IsOwner (physics + hits stay low-latency).
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI))]
    public static class ClientMonsterAI_UpdateAI_Patch
    {
        public static long UpdateHits { get; private set; }
        public static long DriveHits { get; private set; }
        public static long SchemaRejects { get; private set; }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MonsterAI.UpdateAI))]
        public static bool Prefix(MonsterAI __instance, float dt)
        {
            UpdateHits++;

            if (ZNet.instance != null && ZNet.instance.IsDedicated())
                return true;

            if (!IntentZdoSync.IsNetOwner(__instance))
                return true;

            if (!IntentZdoSync.TryReadFromMonsterAI(__instance, Time.time, out var intent))
            {
                // Track schema rejects via shared counter
                if (IntentZdoSync.SchemaMismatches > SchemaRejects)
                    SchemaRejects = IntentZdoSync.SchemaMismatches;
                return true; // no FT intent → vanilla
            }

            CombatDriver.Drive(__instance, intent, dt);
            DriveHits++;
            return false; // sole brain on owner
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
            if (!IntentZdoSync.TryReadFromMonsterAI(mai, Time.time, out var intent))
                return true;
            if (intent.AllowVanillaChase)
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
}
#endif
