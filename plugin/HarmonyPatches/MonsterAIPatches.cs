using System;
using System.Collections.Generic;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Util;
using HarmonyLib;

#if VALHEIM_REFS
using UnityEngine;
#endif

namespace FactionTactics.HarmonyPatches
{
    /// <summary>
    /// Harmony entry + MonsterAI / BaseAI steering + discovery patches.
    /// 0.1.8: also postfix BaseAI.UpdateAI for dedicated diagnostics (baseAIUpdateHits).
    /// </summary>
    public static class MonsterAIPatches
    {
        public static void Apply(Harmony harmony)
        {
#if VALHEIM_REFS
            harmony.PatchAll(typeof(MonsterAIPatches).Assembly);
            Plugin.Log.LogInfo(
                "MonsterAI Harmony patches LIVE (VALHEIM_REFS 0.1.8): MonsterAI.UpdateAI + BaseAI.UpdateAI postfixes; " +
                "CallMoveTo for formation slots; HoldGround Front StopMoving; " +
                "registry via OnEnable/Awake/AddInstance + BaseAI/AnimalAI. Needs in-game smoke test.");
#else
            _ = harmony;
            Plugin.Log.LogInfo("VALHEIM_REFS not set — Harmony MonsterAI patches skipped (stubs mode).");
#endif
        }
    }

#if VALHEIM_REFS
    /// <summary>
    /// Postfix after vanilla UpdateAI: apply HoldGround / formation MoveTo from MemberIntent.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI))]
    public static class MonsterAI_UpdateAI_Patch
    {
        private const float MoveArriveDist = 1.5f;
        private const float AdvanceStopDist = 2f;

        /// <summary>
        /// Incremented on every MonsterAI.UpdateAI postfix entry (even with no intent).
        /// Heartbeat: <c>updateAIHits=</c>.
        /// </summary>
        public static long UpdateAIHitCount { get; private set; }

        public static void ResetUpdateAIHitCount() => UpdateAIHitCount = 0;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(MonsterAI.UpdateAI))]
        public static void UpdateAI_Postfix(MonsterAI __instance, float dt)
        {
            UpdateAIHitCount++;
            MonsterAIRegistry.Register(__instance);

            if (!TryResolveIntent(__instance, out var intent))
                return;

            if (intent.HoldGround)
            {
                __instance.StopMoving();
                return;
            }

            var dest = intent.DesiredPosition;
            if (dest == Vector3.zero && intent.AllowVanillaChase)
            {
                var target = __instance.GetTargetCreature();
                if (target != null)
                    dest = target.transform.position;
            }

            var lineFront = IsLineFront(intent);
            if (lineFront && !intent.AllowVanillaChase)
            {
                CallMoveTo(__instance, dt, dest, MoveArriveDist, intent.PreferRun);
                var pos = __instance.transform.position;
                var dx = pos.x - dest.x;
                var dz = pos.z - dest.z;
                var dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist < AdvanceStopDist)
                    __instance.StopMoving();
                return;
            }

            CallMoveTo(__instance, dt, dest, MoveArriveDist, intent.PreferRun);

            if (intent.PreferKeepRange && !intent.AllowVanillaChase)
            {
                var target = __instance.GetTargetCreature();
                if (target != null)
                    CallLookAt(__instance, target.transform.position);
            }
        }

        private static bool IsLineFront(MemberIntent intent)
        {
            var role = intent.Role == SquadRole.Front || intent.Role == SquadRole.Leader;
            if (!role)
                return false;
            var form = intent.Formation == FormationType.ShieldWall
                       || intent.Formation == FormationType.Line;
            if (!form)
                return false;
            var order = intent.OrderKind == DoctrineOrderKind.Hold
                        || intent.OrderKind == DoctrineOrderKind.Advance
                        || intent.OrderKind == DoctrineOrderKind.ProtectMissiles;
            return order;
        }

        private static bool TryResolveIntent(MonsterAI ai, out MemberIntent intent)
        {
            intent = null!;
            var ch = ValheimIds.GetCharacter(ai);
            if (ch == null)
                return false;
            var id = ValheimIds.FromCharacter(ch);
            if (id == 0)
                return false;
            return OrderApplicator.TryGetIntent(id, out intent);
        }

        private static bool CallMoveTo(BaseAI ai, float dt, Vector3 point, float dist, bool run)
        {
            return Traverse.Create(ai)
                .Method("MoveTo", new object[] { dt, point, dist, run })
                .GetValue<bool>();
        }

        private static void CallLookAt(BaseAI ai, Vector3 point)
        {
            Traverse.Create(ai).Method("LookAt", new object[] { point }).GetValue();
        }
    }

    /// <summary>
    /// Postfix on BaseAI.UpdateAI — MonsterAI/AnimalAI both call base.UpdateAI.
    /// If baseAIUpdateHits&gt;0 but updateAIHits=0 → type/patch issue on MonsterAI;
    /// if both 0 → AI Update is not running in this process the way we expect.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI))]
    public static class BaseAI_UpdateAI_Patch
    {
        public static long BaseAIUpdateHitCount { get; private set; }

        public static void ResetBaseAIUpdateHitCount() => BaseAIUpdateHitCount = 0;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(BaseAI.UpdateAI))]
        public static void UpdateAI_Postfix(BaseAI __instance, float dt)
        {
            BaseAIUpdateHitCount++;
            MonsterAIRegistry.RegisterBase(__instance);
        }
    }

    /// <summary>
    /// Own AI registry — dedicated often has empty s_characters / FindObjectsOfType.
    /// Holds MonsterAI for steering; also tracks BaseAI/AnimalAI for diagnostics.
    /// </summary>
    public static class MonsterAIRegistry
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<MonsterAI> Live = new HashSet<MonsterAI>();
        private static readonly HashSet<BaseAI> LiveBase = new HashSet<BaseAI>();
        private static int _animalAiPeak;

        public static int Count
        {
            get { lock (Gate) return Live.Count; }
        }

        public static int BaseCount
        {
            get { lock (Gate) return LiveBase.Count; }
        }

        /// <summary>Live AnimalAI currently in LiveBase (snapshot count).</summary>
        public static int AnimalAiCount
        {
            get
            {
                lock (Gate)
                {
                    var n = 0;
                    foreach (var b in LiveBase)
                    {
                        try
                        {
                            if (b is AnimalAI)
                                n++;
                        }
                        catch { /* skip */ }
                    }
                    return n;
                }
            }
        }

        public static int AnimalAiPeak
        {
            get { lock (Gate) return _animalAiPeak; }
        }

        public static void Register(MonsterAI? ai)
        {
            if (ai == null)
                return;
            lock (Gate)
            {
                Live.Add(ai);
                LiveBase.Add(ai);
            }
        }

        public static void RegisterBase(BaseAI? ai)
        {
            if (ai == null)
                return;
            if (ai is MonsterAI mai)
            {
                Register(mai);
                return;
            }

            lock (Gate)
            {
                LiveBase.Add(ai);
                if (ai is AnimalAI)
                {
                    var n = 0;
                    foreach (var b in LiveBase)
                    {
                        try
                        {
                            if (b is AnimalAI)
                                n++;
                        }
                        catch { /* skip */ }
                    }
                    if (n > _animalAiPeak)
                        _animalAiPeak = n;
                }
            }
        }

        public static void Unregister(MonsterAI? ai)
        {
            if (ai == null)
                return;
            lock (Gate)
            {
                Live.Remove(ai);
                LiveBase.Remove(ai);
            }
        }

        public static void UnregisterBase(BaseAI? ai)
        {
            if (ai == null)
                return;
            if (ai is MonsterAI mai)
            {
                Unregister(mai);
                return;
            }

            lock (Gate)
                LiveBase.Remove(ai);
        }

        public static void RegisterFromComponent(Component? c)
        {
            if (c == null)
                return;
            try
            {
                MonsterAI? mai = c as MonsterAI;
                if (mai == null)
                {
                    try { mai = c.GetComponent<MonsterAI>(); } catch { /* ignore */ }
                }
                if (mai == null)
                {
                    try { mai = c.GetComponentInChildren<MonsterAI>(true); } catch { /* ignore */ }
                }
                if (mai != null)
                {
                    Register(mai);
                    return;
                }

                BaseAI? bai = c as BaseAI;
                if (bai == null)
                {
                    try { bai = c.GetComponent<BaseAI>(); } catch { /* ignore */ }
                }
                if (bai == null)
                {
                    try { bai = c.GetComponentInChildren<BaseAI>(true); } catch { /* ignore */ }
                }
                if (bai != null)
                {
                    RegisterBase(bai);
                    return;
                }

                Character? ch = c as Character;
                if (ch == null)
                {
                    try { ch = c.GetComponent<Character>(); } catch { /* ignore */ }
                }
                if (ch == null)
                {
                    try { ch = c.GetComponentInChildren<Character>(true); } catch { /* ignore */ }
                }
                if (ch != null)
                {
                    try
                    {
                        var fromCh = ch.GetBaseAI();
                        if (fromCh != null)
                            RegisterBase(fromCh);
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>Copy live MonsterAI entries; prune destroyed/null.</summary>
        public static List<MonsterAI> Snapshot()
        {
            lock (Gate)
            {
                var list = new List<MonsterAI>(Live.Count);
                Live.RemoveWhere(ai => ai == null);
                LiveBase.RemoveWhere(ai => ai == null);
                foreach (var ai in Live)
                {
                    try
                    {
                        if (ai == null)
                            continue;
                        list.Add(ai);
                    }
                    catch { /* skip */ }
                }
                return list;
            }
        }

        public static List<BaseAI> SnapshotBase()
        {
            lock (Gate)
            {
                var list = new List<BaseAI>(LiveBase.Count);
                LiveBase.RemoveWhere(ai => ai == null);
                foreach (var ai in LiveBase)
                {
                    try
                    {
                        if (ai == null)
                            continue;
                        list.Add(ai);
                    }
                    catch { /* skip */ }
                }
                return list;
            }
        }
    }

    [HarmonyPatch(typeof(BaseAI), "OnEnable")]
    public static class BaseAI_OnEnable_RegistryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseAI __instance)
            => MonsterAIRegistry.RegisterBase(__instance);
    }

    [HarmonyPatch(typeof(BaseAI), "OnDisable")]
    public static class BaseAI_OnDisable_RegistryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseAI __instance)
            => MonsterAIRegistry.UnregisterBase(__instance);
    }

    [HarmonyPatch(typeof(Character), "Awake")]
    public static class Character_Awake_RegistryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Character __instance)
            => MonsterAIRegistry.RegisterFromComponent(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), "AddInstance")]
    public static class ZNetScene_AddInstance_RegistryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ZDO zdo, ZNetView nview)
            => MonsterAIRegistry.RegisterFromComponent(nview);
    }

    /// <summary>
    /// Optional BaseAI.MoveTo Prefix — intentionally unbound.
    /// </summary>
    public static class BaseAI_MoveTo_Patch
    {
        // Intentionally unbound — do not add [HarmonyPatch] until needed.
    }
#endif
}
