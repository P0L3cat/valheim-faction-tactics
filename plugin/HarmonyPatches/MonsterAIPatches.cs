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
    /// 0.1.10: registry by instance-id (no OnDisable clear); also postfix BaseAI.UpdateAI for dedicated diagnostics (baseAIUpdateHits).
    /// </summary>
    public static class MonsterAIPatches
    {
        public static void Apply(Harmony harmony)
        {
#if VALHEIM_REFS
            harmony.PatchAll(typeof(MonsterAIPatches).Assembly);
            Plugin.Log.LogInfo(
                "MonsterAI Harmony patches LIVE (VALHEIM_REFS 0.1.10): MonsterAI.UpdateAI + BaseAI.UpdateAI postfixes; " +
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
    /// 0.1.10: store by Unity instance ID (HashSet+Unity== was dropping live MAs);
    /// do NOT unregister on OnDisable (MonoUpdaters still runs UpdateAI while enable flickers);
    /// unregister only on OnDestroy. UpdateAI postfix keeps re-registering.
    /// </summary>
    public static class MonsterAIRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, MonsterAI> Live = new Dictionary<int, MonsterAI>();
        private static readonly Dictionary<int, BaseAI> LiveBase = new Dictionary<int, BaseAI>();
        private static int _animalAiPeak;
        private static int _registerEvents;
        private static int _unregisterDestroyEvents;
        private static bool _loggedFirstRegister;

        public static int Count
        {
            get { lock (Gate) { PruneUnlocked(); return Live.Count; } }
        }

        public static int BaseCount
        {
            get { lock (Gate) { PruneUnlocked(); return LiveBase.Count; } }
        }

        /// <summary>Live AnimalAI currently in LiveBase (snapshot count).</summary>
        public static int AnimalAiCount
        {
            get
            {
                lock (Gate)
                {
                    PruneUnlocked();
                    var n = 0;
                    foreach (var b in LiveBase.Values)
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

        /// <summary>True only when the managed wrapper is gone (not Unity fake-null alone).</summary>
        private static bool IsManagedNull(UnityEngine.Object? obj)
            => ReferenceEquals(obj, null);

        /// <summary>Unity destroyed / missing component (overloaded ==).</summary>
        private static bool IsUnityDestroyed(UnityEngine.Object? obj)
            => !ReferenceEquals(obj, null) && obj == null;

        private static void PruneUnlocked()
        {
            if (Live.Count > 0)
            {
                var dead = new List<int>();
                foreach (var kv in Live)
                {
                    if (IsManagedNull(kv.Value) || IsUnityDestroyed(kv.Value))
                        dead.Add(kv.Key);
                }
                foreach (var id in dead)
                    Live.Remove(id);
            }

            if (LiveBase.Count > 0)
            {
                var deadB = new List<int>();
                foreach (var kv in LiveBase)
                {
                    if (IsManagedNull(kv.Value) || IsUnityDestroyed(kv.Value))
                        deadB.Add(kv.Key);
                }
                foreach (var id in deadB)
                    LiveBase.Remove(id);
            }
        }

        public static void Register(MonsterAI? ai)
        {
            if (IsManagedNull(ai) || IsUnityDestroyed(ai))
                return;
            int id;
            try { id = ai!.GetInstanceID(); }
            catch { return; }
            if (id == 0)
                return;

            lock (Gate)
            {
                Live[id] = ai!;
                LiveBase[id] = ai!;
                _registerEvents++;
                if (!_loggedFirstRegister)
                {
                    _loggedFirstRegister = true;
                    Plugin.Log?.LogInfo(
                        $"MonsterAIRegistry: first Register id={id} name={SafeName(ai)} " +
                        $"(UpdateAI re-registers; OnDisable no longer clears).");
                }
            }
        }

        public static void RegisterBase(BaseAI? ai)
        {
            if (IsManagedNull(ai) || IsUnityDestroyed(ai))
                return;
            if (ai is MonsterAI mai)
            {
                Register(mai);
                return;
            }

            int id;
            try { id = ai!.GetInstanceID(); }
            catch { return; }
            if (id == 0)
                return;

            lock (Gate)
            {
                LiveBase[id] = ai!;
                if (ai is AnimalAI)
                {
                    var n = 0;
                    foreach (var b in LiveBase.Values)
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
            if (IsManagedNull(ai))
                return;
            int id;
            try { id = ai!.GetInstanceID(); }
            catch { return; }
            lock (Gate)
            {
                Live.Remove(id);
                LiveBase.Remove(id);
                _unregisterDestroyEvents++;
            }
        }

        public static void UnregisterBase(BaseAI? ai)
        {
            if (IsManagedNull(ai))
                return;
            if (ai is MonsterAI mai)
            {
                Unregister(mai);
                return;
            }

            int id;
            try { id = ai!.GetInstanceID(); }
            catch { return; }
            lock (Gate)
                LiveBase.Remove(id);
        }

        public static void RegisterFromComponent(Component? c)
        {
            if (IsManagedNull(c) || IsUnityDestroyed(c))
                return;
            try
            {
                MonsterAI? mai = c as MonsterAI;
                if (mai == null)
                {
                    try { mai = c!.GetComponent<MonsterAI>(); } catch { /* ignore */ }
                }
                if (mai == null)
                {
                    try { mai = c!.GetComponentInChildren<MonsterAI>(true); } catch { /* ignore */ }
                }
                if (mai != null)
                {
                    Register(mai);
                    return;
                }

                BaseAI? bai = c as BaseAI;
                if (bai == null)
                {
                    try { bai = c!.GetComponent<BaseAI>(); } catch { /* ignore */ }
                }
                if (bai == null)
                {
                    try { bai = c!.GetComponentInChildren<BaseAI>(true); } catch { /* ignore */ }
                }
                if (bai != null)
                {
                    RegisterBase(bai);
                    return;
                }

                Character? ch = c as Character;
                if (ch == null)
                {
                    try { ch = c!.GetComponent<Character>(); } catch { /* ignore */ }
                }
                if (ch == null)
                {
                    try { ch = c!.GetComponentInChildren<Character>(true); } catch { /* ignore */ }
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
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning(
                    $"MonsterAIRegistry.RegisterFromComponent failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Copy live MonsterAI entries; prune destroyed/null.</summary>
        public static List<MonsterAI> Snapshot()
        {
            lock (Gate)
            {
                PruneUnlocked();
                var list = new List<MonsterAI>(Live.Count);
                foreach (var ai in Live.Values)
                {
                    try
                    {
                        if (IsManagedNull(ai) || IsUnityDestroyed(ai))
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
                PruneUnlocked();
                var list = new List<BaseAI>(LiveBase.Count);
                foreach (var ai in LiveBase.Values)
                {
                    try
                    {
                        if (IsManagedNull(ai) || IsUnityDestroyed(ai))
                            continue;
                        list.Add(ai);
                    }
                    catch { /* skip */ }
                }
                return list;
            }
        }

        public static string Diagnostics()
        {
            lock (Gate)
            {
                PruneUnlocked();
                return $"live={Live.Count} liveBase={LiveBase.Count} registerEvents={_registerEvents} " +
                       $"destroyUnregisters={_unregisterDestroyEvents}";
            }
        }

        private static string SafeName(MonsterAI? ai)
        {
            try { return ai != null && ai.gameObject != null ? ai.gameObject.name : "?"; }
            catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(BaseAI), "OnEnable")]
    public static class BaseAI_OnEnable_RegistryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseAI __instance)
            => MonsterAIRegistry.RegisterBase(__instance);
    }

    /// <summary>
    /// Intentionally does NOT unregister. On dedicated, BaseAI can disable/enable while
    /// MonoUpdaters still invokes UpdateAI; clearing here emptied registry while updateAIHits climbed.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), "OnDisable")]
    public static class BaseAI_OnDisable_RegistryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BaseAI __instance)
        {
            // Keep registry entries; UpdateAI + ownership director re-assert registration.
            _ = __instance;
        }
    }

    [HarmonyPatch(typeof(BaseAI), "OnDestroy")]
    public static class BaseAI_OnDestroy_RegistryPatch
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
