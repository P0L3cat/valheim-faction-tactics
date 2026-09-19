#if VALHEIM_REFS
using System;
using FactionTactics.Ambience;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Util;
using HarmonyLib;
using UnityEngine;

namespace FactionTactics.Combat
{
    /// <summary>
    /// Shared owning-peer combat driver (0.3.0). Used by server listen-host executor
    /// and FactionTactics.Client. Keeps MoveTo/Hold/LookAt/DoAttack on the ZDO owner.
    /// </summary>
    public static class CombatDriver
    {
        private const float MoveArriveDist = 1.5f;
        private const float AdvanceStopDist = 2f;

        public static long DriveCount { get; private set; }
        public static long AttackAttempts { get; private set; }

        public static void Drive(MonsterAI ai, MemberIntent intent, float dt)
        {
            DriveCount++;
            PulseDeathRushAudio(ai, intent);
            CallUpdateTarget(ai, dt);

            var lineFront = IsLineFront(intent);

            if (intent.HoldGround)
            {
                ai.StopMoving();
                FaceThreatOrSlot(ai, intent);
                SoftSuppressHunt(ai);
                TryDriveAttack(ai, intent);
                return;
            }

            var dest = intent.DesiredPosition;
            if (dest == Vector3.zero && intent.AllowVanillaChase)
            {
                var target = ai.GetTargetCreature();
                if (target != null)
                    dest = target.transform.position;
            }

            if (lineFront && !intent.AllowVanillaChase)
            {
                CallMoveTo(ai, dt, dest, MoveArriveDist, intent.PreferRun);
                var pos = ai.transform.position;
                var dx = pos.x - dest.x;
                var dz = pos.z - dest.z;
                var dist = (float)Math.Sqrt(dx * dx + dz * dz);
                if (dist < AdvanceStopDist)
                    ai.StopMoving();
                FaceThreatOrSlot(ai, intent);
                SoftSuppressHunt(ai);
                TryDriveAttack(ai, intent);
                return;
            }

            if (dest != Vector3.zero)
                CallMoveTo(ai, dt, dest, MoveArriveDist, intent.PreferRun);

            if (intent.PreferKeepRange && !intent.AllowVanillaChase)
            {
                var target = ai.GetTargetCreature();
                if (target != null)
                    CallLookAt(ai, target.transform.position);
            }
            else if (intent.AllowVanillaChase)
            {
                FaceThreatOrSlot(ai, intent);
            }

            TryDriveAttack(ai, intent);
        }

        /// <summary>
        /// Front/Leader on ShieldWall/Line for Hold / Advance / ProtectMissiles.
        /// </summary>
        public static bool IsLineFront(MemberIntent intent)
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

        /// <summary>
        /// 0.1.11 leftover — target-nulling chase suppress. Kept for diagnostics/callers;
        /// controlled path uses SoftSuppressHunt instead so DoAttack still has a target.
        /// </summary>
        public static void SuppressVanillaChase(MonsterAI ai, MemberIntent intent)
        {
            if (intent.AllowVanillaChase)
                return;
            try
            {
                var tr = Traverse.Create(ai);
                tr.Field("m_targetCreature").SetValue(null);
                tr.Field("m_targetStatic").SetValue(null);
                var slot = intent.DesiredPosition;
                if (slot == Vector3.zero)
                    slot = ai.transform.position;
                tr.Field("m_lastKnownTargetPos").SetValue(slot);
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug(
                    $"SuppressVanillaChase Traverse failed: {ex.GetType().Name}: {ex.Message}");
            }

            SoftSuppressHunt(ai);
        }

        /// <summary>Clear HuntPlayer only — do not wipe combat target.</summary>
        public static void SoftSuppressHunt(MonsterAI ai)
        {
            try
            {
                if (ai.HuntPlayer())
                    ai.SetHuntPlayer(false);
            }
            catch
            {
                // HuntPlayer/SetHuntPlayer public on BaseAI — ignore rare failures
            }
        }

        private static void FaceThreatOrSlot(MonsterAI ai, MemberIntent intent)
        {
            try
            {
                var target = ai.GetTargetCreature();
                if (target != null)
                {
                    CallLookAt(ai, target.transform.position);
                    return;
                }
            }
            catch { /* fall through */ }

            if (intent.DesiredPosition != Vector3.zero)
                CallLookAt(ai, intent.DesiredPosition);
        }

        /// <summary>
        /// Private MonsterAI.UpdateTarget(Humanoid, float, out bool, out bool) — sense without vanilla move/attack.
        /// </summary>
        private static void CallUpdateTarget(MonsterAI ai, float dt)
        {
            try
            {
                var ch = ValheimIds.GetCharacter(ai);
                if (!(ch is Humanoid humanoid))
                    return;
                // UpdateTarget(Humanoid, float, out bool canHear, out bool canSee) — private
                var mi = AccessTools.Method(typeof(MonsterAI), "UpdateTarget");
                if (mi == null)
                    return;
                var args = new object[] { humanoid, dt, false, false };
                mi.Invoke(ai, args);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Drive attack when FT owns the brain. Skip pure withdraw; missiles may still fire on Kite.
        /// </summary>
        private static void TryDriveAttack(MonsterAI ai, MemberIntent intent)
        {
            if (intent.OrderKind == DoctrineOrderKind.RetreatAndReform)
                return;
            if (intent.OrderKind == DoctrineOrderKind.Kite
                && intent.Role != SquadRole.Missile)
                return;

            try
            {
                var target = ai.GetTargetCreature();
                if (target == null)
                    return;
                // DoAttack(Character target, bool isFriend) — private; isFriend=false for hostiles.
                var mi = AccessTools.Method(typeof(MonsterAI), "DoAttack");
                if (mi == null)
                    return;
                mi.Invoke(ai, new object[] { target, false });
            }
            catch (Exception)
            {
            }
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

        static void PulseDeathRushAudio(MonsterAI ai, MemberIntent intent)
        {
            if (intent == null || !intent.DeathRush)
                return;
#if !FT_CLIENT
            // Server/listen-host: honor scream config. Client always plays when flag set.
            if (FactionTactics.Config.PluginConfig.EnableDeathRushScream?.Value == false)
                return;
#endif
            DeathRushAudio.PulseIfNeeded(ai, intent);
        }

    }
}
#endif
