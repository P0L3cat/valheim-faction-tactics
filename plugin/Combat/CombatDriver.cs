#if VALHEIM_REFS
using System;
using System.Collections.Concurrent;
using FactionTactics.Ambience;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using FactionTactics.Util;
using HarmonyLib;
using UnityEngine;

namespace FactionTactics.Combat
{
    /// <summary>
    /// Shared owning-peer combat driver (1.0.5 Phase A). Used by server listen-host executor
    /// and FactionTactics.Client. Keeps MoveTo/Hold/LookAt/gated DoAttack on the ZDO owner.
    /// </summary>
    public static class CombatDriver
    {
        private const float MoveArriveDist = 1.5f;
        private const float AdvanceStopDist = 2f;
        private const float DefaultMeleeRange = 2.5f;
        /// <summary>Matches OrderApplicator.FrontHoldSlotDist. Local so the client build can drop a stale pin.</summary>
        private const float HoldGroundPinDist = 2.5f;

        public static long DriveCount { get; private set; }
        public static long AttackAttempts { get; private set; }
        /// <summary>Successful DoAttack invokes after the swing gate.</summary>
        public static long Swings { get; private set; }
        /// <summary>Hold/Protect Front swings blocked by the gate (cooldown/range/anim).</summary>
        public static long HoldBlocks { get; private set; }

        private static readonly ConcurrentDictionary<long, float> NextSwingTime
            = new ConcurrentDictionary<long, float>();

        public static void ResetCounters()
        {
            DriveCount = AttackAttempts = Swings = HoldBlocks = 0;
            NextSwingTime.Clear();
        }

        public static void Drive(MonsterAI ai, MemberIntent intent, float dt)
        {
            DriveCount++;
            PulseDeathRushAudio(ai, intent);
            CallUpdateTarget(ai, dt);

            var lineFront = IsLineFront(intent);

            if (intent.HoldGround)
            {
                // Slot pin with the destination still far away is the eternal-wall failure.
                // Drop the flag and walk; the next intent refresh puts it back only when legal.
                if (HoldGroundDestinationFar(ai, intent))
                    intent.HoldGround = false;
                else
                {
                    ai.StopMoving();
                    FaceThreatOrSlot(ai, intent);
                    SoftSuppressHunt(ai);
                    TryDriveAttack(ai, intent);
                    return;
                }
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

        public static void SoftSuppressHunt(MonsterAI ai)
        {
            try
            {
                if (ai.HuntPlayer())
                    ai.SetHuntPlayer(false);
            }
            catch
            {
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

        private static void CallUpdateTarget(MonsterAI ai, float dt)
        {
            try
            {
                var ch = ValheimIds.GetCharacter(ai);
                if (!(ch is Humanoid humanoid))
                    return;
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
        /// Phase A swing gate: DoAttack only when target exists, in weapon band, cooldown
        /// (+ slot stagger) elapsed, and not mid-attack/recovery. Same path for Hold /
        /// ProtectMissiles Front and other owning-peer drives.
        /// </summary>
        private static void TryDriveAttack(MonsterAI ai, MemberIntent intent)
        {
            if (intent.OrderKind == DoctrineOrderKind.RetreatAndReform)
                return;
            if (intent.OrderKind == DoctrineOrderKind.Kite
                && intent.Role != SquadRole.Missile)
                return;

            var gatedHold = intent.HoldGround
                            || (IsLineFront(intent)
                                && (intent.OrderKind == DoctrineOrderKind.Hold
                                    || intent.OrderKind == DoctrineOrderKind.ProtectMissiles));

            try
            {
                var target = ai.GetTargetCreature();
                if (target == null)
                {
                    if (gatedHold)
                        HoldBlocks++;
                    return;
                }

                if (IsMidAttack(ai))
                {
                    if (gatedHold)
                        HoldBlocks++;
                    return;
                }

                if (!IsInWeaponBand(ai, target, intent))
                {
                    if (gatedHold)
                        HoldBlocks++;
                    return;
                }

                var id = InstanceId(ai);
                var now = Time.time;
                var cooldown = EffectiveCooldown(ai, intent);
                if (NextSwingTime.TryGetValue(id, out var next) && now < next)
                {
                    if (gatedHold)
                        HoldBlocks++;
                    return;
                }

                AttackAttempts++;
                var mi = AccessTools.Method(typeof(MonsterAI), "DoAttack");
                if (mi == null)
                    return;
                mi.Invoke(ai, new object[] { target, false });
                Swings++;
                NextSwingTime[id] = now + cooldown;
            }
            catch (Exception)
            {
            }
        }

        private static float EffectiveCooldown(MonsterAI ai, MemberIntent intent)
        {
            var baseCd = CombatTuning.HoldAttackCooldown;
            try
            {
                var minInterval = Traverse.Create(ai).Field("m_minAttackInterval").GetValue<float>();
                if (minInterval > baseCd)
                    baseCd = minInterval;
            }
            catch { /* keep config floor */ }

            var stagger = CombatTuning.SwingStaggerMs * Math.Max(0, intent.SlotIndex) / 1000f;
            if (intent.Role == SquadRole.Missile)
                stagger += 0.05f;
            return baseCd + stagger;
        }

        private static bool IsMidAttack(MonsterAI ai)
        {
            try
            {
                var ch = ValheimIds.GetCharacter(ai);
                if (ch != null)
                {
                    var inAttack = AccessTools.Method(ch.GetType(), "InAttack");
                    if (inAttack != null && inAttack.GetParameters().Length == 0)
                    {
                        if ((bool)inAttack.Invoke(ch, null))
                            return true;
                    }
                }
            }
            catch { /* fall through */ }

            try
            {
                var was = Traverse.Create(ai).Field("m_wasInAttack").GetValue<bool>();
                if (was)
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        private static bool IsInWeaponBand(MonsterAI ai, Character target, MemberIntent intent)
        {
            try
            {
                var origin = ai.transform.position;
                var dest = target.transform.position;
                var dx = origin.x - dest.x;
                var dz = origin.z - dest.z;
                var dist = (float)Math.Sqrt(dx * dx + dz * dz);

                float maxRange = DefaultMeleeRange;
                float minRange = 0f;
                try
                {
                    var tr = Traverse.Create(ai);
                    maxRange = tr.Field("m_aiAttackRange").GetValue<float>();
                    if (maxRange <= 0.01f)
                        maxRange = DefaultMeleeRange;
                    try
                    {
                        minRange = tr.Field("m_aiAttackRangeMin").GetValue<float>();
                    }
                    catch { minRange = 0f; }
                }
                catch
                {
                    maxRange = DefaultMeleeRange;
                }

                // Prefer SelectBestAttack range when available (weapon-aware).
                try
                {
                    var select = AccessTools.Method(typeof(MonsterAI), "SelectBestAttack");
                    // Signature varies; best-effort — keep m_aiAttackRange as primary.
                    _ = select;
                }
                catch { /* ignore */ }

                var factor = CombatTuning.HoldAttackRangeFactor;
                if (factor < 0.25f)
                    factor = 0.25f;
                maxRange *= factor;

                // Missiles / PreferKeepRange: allow a wider band so they can still soft-fire.
                if (intent.PreferKeepRange || intent.Role == SquadRole.Missile)
                {
                    if (maxRange < 8f)
                        maxRange = 12f;
                }

                if (dist > maxRange)
                    return false;
                if (minRange > 0.1f && dist < minRange * 0.5f && intent.PreferKeepRange)
                    return false;
                return true;
            }
            catch
            {
                return true; // fail open for Charge/chase; Hold gated path already counted blocks on null
            }
        }

        private static bool HoldGroundDestinationFar(MonsterAI ai, MemberIntent intent)
        {
            var dest = intent.DesiredPosition;
            if (dest == Vector3.zero || ai == null)
                return false;
            var pos = ai.transform.position;
            var dx = pos.x - dest.x;
            var dz = pos.z - dest.z;
            return dx * dx + dz * dz > HoldGroundPinDist * HoldGroundPinDist;
        }

        private static long InstanceId(MonsterAI ai)
        {
            try
            {
                var ch = ValheimIds.GetCharacter(ai);
                var id = ValheimIds.FromCharacter(ch);
                if (id != 0)
                    return id;
            }
            catch { /* fall through */ }
            return ai != null ? ai.GetInstanceID() : 0;
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
            if (FactionTactics.Config.PluginConfig.EnableDeathRushScream?.Value == false)
                return;
#endif
            DeathRushAudio.PulseIfNeeded(ai, intent);
        }

    }
}
#endif
