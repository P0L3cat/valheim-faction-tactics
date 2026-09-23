using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Squad;
using UnityEngine;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Translates <see cref="SquadOrder"/> into per-member intent consumed by Harmony patches.
    /// Siege Assault role split (when AssaultActive + players present):
    ///   • Wall-breakers (Front / Leader / melee Flanker) → press structure / TestBreach
    ///   • Missiles → FocusWallman cover (ProtectMissiles / FocusFire on players threatening breachers)
    /// Quiet assault (AllowVanillaStructure): light-touch — PreferAllowVanillaStructure, don't override chase away from pieces.
    /// ShieldWall/Line slots are oriented centroid→threat (right = lateral, forward = depth).
    /// </summary>
    public sealed class OrderApplicator
    {
        /// <summary>Front/Leader may pin HoldGround only inside this of their slot (meters).</summary>
        public const float FrontHoldSlotDist = 2.5f;

        /// <summary>Other doctrines: HoldGround only when the threat is already in melee.</summary>
        public const float GenericContactBand = 3.5f;

        /// <summary>Last applied order keyed by MonsterAI instance id (or hash).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent> Intents
            = new System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent>();

        public void Apply(SquadUnit squad, SquadOrder order, Squad.SquadRuntimeState? runtime = null)
        {
            var centroid = ComputeCentroid(squad);
            var doctrineId = squad.Doctrine?.Id ?? "";
            var jelly = string.Equals(doctrineId, "artillery-jelly", System.StringComparison.OrdinalIgnoreCase);
            var charred = string.Equals(doctrineId, "charred-legion", System.StringComparison.OrdinalIgnoreCase);
            var roman = string.Equals(doctrineId, "roman", System.StringComparison.OrdinalIgnoreCase);
            var viking = string.Equals(doctrineId, "viking-shieldwall", System.StringComparison.OrdinalIgnoreCase);
            var ambush = string.Equals(doctrineId, "ambush", System.StringComparison.OrdinalIgnoreCase);
            var deathRush = string.Equals(doctrineId, "death-rush", System.StringComparison.OrdinalIgnoreCase);

            // Phase A slot lock: stable indices + holes for dead (no per-death equal-spacing rebuild).
            var casualty = squad.LastCasualtyRatio;
            int capacity;
            if (runtime != null)
            {
                capacity = FormationSlotLock.EnsureSlots(runtime, squad, order, casualty);
            }
            else
            {
                // Offline / tests without runtime: dense living indices.
                capacity = 0;
                foreach (var m in squad.Members)
                    if (m.IsAlive) capacity++;
                capacity = System.Math.Max(1, capacity);
            }

            // Threat-facing basis for ShieldWall / Line (centroid → threat / nearest player).
            var threatPos = TryGetSquadThreatPosition(squad, centroid);
            BuildFacingBasis(centroid, threatPos, out var right, out var forward);

            var cadencePhase = runtime?.RomanPhase ?? RomanPhase.Idle;
            var anchorShift = Vector3.zero;
            if ((roman || viking)
                && threatPos.HasValue
                && (cadencePhase == RomanPhase.ApproachStandoff || cadencePhase == RomanPhase.PressContact))
            {
                var keep = KeepDistance(roman, cadencePhase, runtime);
                anchorShift = ComputeAnchorShift(centroid, threatPos.Value, keep);
            }

            var distToThreat = ResolveThreatDistance(squad, runtime);

            var fallbackIndex = 0;
            foreach (var member in squad.Members)
            {
                if (!member.IsAlive)
                    continue;

                var isCavalry = charred && Contains(member.PrefabName, "Asksvin");
                var isArtillery = jelly
                    || Contains(member.PrefabName, "Gjall")
                    || (member.AssignedRole == SquadRole.Missile && jelly);

                int slotIndex;
                if (runtime != null && FormationSlotLock.TryGetSlot(runtime, member.InstanceId, out slotIndex))
                {
                    // locked
                }
                else
                {
                    slotIndex = fallbackIndex;
                }

                var slot = FormationSlot(
                    order.Formation,
                    member.AssignedRole,
                    slotIndex,
                    capacity,
                    centroid,
                    member.Position,
                    isCavalry,
                    isArtillery || jelly,
                    right,
                    forward);

                var ambushHarassment = ambush
                    && (order.OrderKind == DoctrineOrderKind.Flank
                        || order.OrderKind == DoctrineOrderKind.Kite
                        || order.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles);

                var keepRange = jelly
                    || order.OrderKind == DoctrineOrderKind.Kite
                    || ambushHarassment
                    || (isArtillery && order.OrderKind != DoctrineOrderKind.Charge);

                // --- Siege Assault role split ---
                var isMissile = member.AssignedRole == SquadRole.Missile;
                var isWallBreaker = order.AssaultActive
                    && !isMissile
                    && (member.AssignedRole == SquadRole.Front
                        || member.AssignedRole == SquadRole.Leader
                        || member.AssignedRole == SquadRole.Flanker
                        || member.LooksLikeHeavy);

                var assaultMissileCover = order.AssaultActive
                    && order.AssaultPlayersPresent
                    && isMissile;

                // Quiet assault: wall-breakers keep vanilla structure targeting (don't fight vanilla).
                var allowVanillaStructure = order.AllowVanillaStructure && isWallBreaker;

                // Hot assault: missiles cover breachers — keep range, no wall chewing.
                if (assaultMissileCover)
                    keepRange = true;

                // Missiles (Roman + others): PreferKeepRange on wall / fire orders — rear slots, no HoldGround.
                if (isMissile
                    && (order.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles
                        || order.OrderKind == DoctrineOrderKind.Hold
                        || order.OrderKind == DoctrineOrderKind.Advance))
                    keepRange = true;

                // HoldGround is decided once, after slot distance is known. Moving orders never pin.
                var allowChase = !keepRange
                    && !jelly
                    && (order.OrderKind == DoctrineOrderKind.Charge
                        || order.OrderKind == DoctrineOrderKind.FocusFire);

                // Asksvin on Flank may chase; jelly FocusFire must not melee-chase into fire.
                if (isCavalry && order.OrderKind == DoctrineOrderKind.Flank)
                    allowChase = true;
                if (jelly)
                    allowChase = false;

                // Ambush doctrine: AllowVanillaChase only on the rare Charge flash.
                // Flank/Kite/FocusFire/ProtectMissiles keep PreferKeepRange and no chase.
                if (ambush)
                    allowChase = order.OrderKind == DoctrineOrderKind.Charge;

                // Death-Rush (Meadows Greyling): bee-line Charge — never HoldGround / PreferKeepRange / kite.
                if (deathRush)
                {
                    keepRange = false;
                    if (order.OrderKind == DoctrineOrderKind.Charge)
                        allowChase = true;
                }

                // Siege: hot wall-breakers chase / press (TestBreach).
                // Quiet assault (AllowVanillaStructure): leave vanilla structure AI alone —
                // PreferAllowVanillaStructure / do not force chase away from pieces.
                if (isWallBreaker)
                {
                    if (order.AllowVanillaStructure)
                    {
                        allowChase = false;
                        keepRange = false;
                    }
                    else
                    {
                        allowChase = true;
                        keepRange = false;
                    }
                }

                // Hot assault missiles: do not chase walls — FocusFire players threatening breachers.
                if (assaultMissileCover)
                {
                    allowChase = order.OrderKind == DoctrineOrderKind.FocusFire
                                 || order.OrderKind == DoctrineOrderKind.ProtectMissiles;
                    // Chase players only, not structures — PreferKeepRange + AllowVanillaChase for combat target.
                }

                // Form up on the slot, then pin HoldGround only in a banded hold phase
                // (or a strict contact Hold for other doctrines). Advance/Charge/Flank/Kite/Retreat
                // never pin, even when distToSlot is 0 — that near-slot rule was the eternal wall.
                var isFrontLine = member.AssignedRole == SquadRole.Front
                                  || member.AssignedRole == SquadRole.Leader;
                var shieldOrLine = order.Formation == FormationType.ShieldWall
                                   || order.Formation == FormationType.Line;
                var movingCadence = (roman || viking)
                    && (cadencePhase == RomanPhase.ApproachStandoff
                        || cadencePhase == RomanPhase.PressContact);
                var distToSlot = HorizontalDistance(member.Position, slot);

                var desired = slot;
                if (movingCadence && !isWallBreaker && !isCavalry)
                {
                    desired = slot + anchorShift;
                    if (isFrontLine && !isMissile)
                        allowChase = false;
                }
                else if (order.OrderKind == DoctrineOrderKind.Charge && !keepRange)
                {
                    var threat = TryGetThreatPosition(member, centroid);
                    if (threat.HasValue)
                        desired = threat.Value;
                }
                else if (!isWallBreaker && !isCavalry && !isMissile && isFrontLine && (roman || viking || shieldOrLine)
                         && (order.OrderKind == DoctrineOrderKind.Hold
                             || order.OrderKind == DoctrineOrderKind.Advance
                             || order.OrderKind == DoctrineOrderKind.ProtectMissiles
                             || order.OrderKind == DoctrineOrderKind.FocusFire))
                {
                    desired = slot;
                    allowChase = false;
                }
                else if (!keepRange
                    && order.OrderKind == DoctrineOrderKind.FocusFire
                    && !roman
                    && !viking)
                {
                    var threat = TryGetThreatPosition(member, centroid);
                    if (threat.HasValue)
                        desired = threat.Value;
                }

                var holdGround = AllowHoldGround(
                    doctrineId,
                    order.OrderKind,
                    member.AssignedRole,
                    isMissile,
                    isCavalry,
                    isWallBreaker,
                    distToThreat,
                    distToSlot,
                    cadencePhase);

                // Hot assault missiles cover players; they do not plant.
                if (assaultMissileCover)
                    holdGround = false;

                var intent = new MemberIntent
                {
                    SquadId = squad.SquadId,
                    OrderKind = order.OrderKind,
                    Formation = order.Formation,
                    Stance = order.Stance,
                    Role = member.AssignedRole,
                    DesiredPosition = desired,
                    FocusTargetId = order.FocusTargetId,
                    HoldGround = holdGround,
                    PreferRun = order.OrderKind == DoctrineOrderKind.Charge
                                || order.OrderKind == DoctrineOrderKind.RetreatAndReform
                                || order.OrderKind == DoctrineOrderKind.Kite
                                || isCavalry
                                || isWallBreaker
                                || deathRush,
                    AllowVanillaChase = allowChase,
                    PreferKeepRange = keepRange,
                    AssaultWallBreaker = isWallBreaker,
                    AssaultMissileCover = assaultMissileCover,
                    AllowVanillaStructure = allowVanillaStructure,
                    DeathRush = deathRush,
                    SlotIndex = slotIndex,
                };

                Intents[member.InstanceId] = intent;
#if VALHEIM_REFS
                // Skip networking in unit-test hosts (Unity ECalls unavailable).
                if (System.AppDomain.CurrentDomain.FriendlyName.IndexOf("testhost", System.StringComparison.OrdinalIgnoreCase) < 0)
                    TryReplicateIntent(member, intent);
#endif
                fallbackIndex++;
            }
        }


#if VALHEIM_REFS
        /// <summary>
        /// 1.0.3: primary path queues intent for ZRoutedRpc broadcast (see IntentRpcSync).
        /// ZDO.Set remains optional/debug fallback — unreliable when dedicated is not ZDO owner.
        /// </summary>
        private static void TryReplicateIntent(SquadMemberView member, MemberIntent intent)
        {
            // Never touch UnityEngine.Time here — unit tests load VALHEIM_REFS assemblies without a Unity runtime.
            // Client IntentRpcSync.OnRouted re-stamps WrittenAt with local Time.time.
            try
            {
                var rpcOn = true;
                try { rpcOn = PluginConfig.EnableRpcIntentSync?.Value != false; } catch { rpcOn = true; }
                if (rpcOn)
                    IntentRpcSync.Queue(member.InstanceId, intent, 0f);

                var zdoOn = false;
                try { zdoOn = PluginConfig.EnableZdoIntentSync?.Value == true; } catch { zdoOn = false; }
                if (!zdoOn)
                    return;

                // ZDO fallback only when explicitly enabled (live game). Best-effort clock.
                float zdoNow = 0f;
                try { zdoNow = UnityEngine.Time.time; } catch { return; }
                if (member.NativeHandle is MonsterAI mai)
                    IntentZdoSync.WriteFromMonsterAI(mai, intent, zdoNow);
                else if (member.NativeHandle is ZDO zdo)
                    IntentZdoSync.Write(zdo, intent, zdoNow);
            }
            catch (System.Exception ex)
            {
                try
                {
                    if (PluginConfig.DebugLogging?.Value == true)
                        Plugin.Log?.LogDebug($"TryReplicateIntent: {ex.GetType().Name}: {ex.Message}");
                }
                catch { /* ignore */ }
            }
        }

        #endif

        public static bool TryGetIntent(long instanceId, out MemberIntent intent)
            => Intents.TryGetValue(instanceId, out intent!);

        /// <summary>
        /// HoldGround is rare. False for missiles, flankers, cavalry, wall-breakers, and every
        /// moving order (Advance / Charge / Flank / Kite / RetreatAndReform), even at distToSlot 0.
        /// Roman and Viking pin only in standoff, contact, or retreat-pause, and only near the slot.
        /// Ambush pins only on Hold, near the slot, with the player inside the harass pocket.
        /// Everyone else: Hold or ProtectMissiles, near the slot, and inside <see cref="GenericContactBand"/>.
        /// </summary>
        public static bool AllowHoldGround(
            string? doctrineId,
            DoctrineOrderKind order,
            SquadRole role,
            bool isMissile,
            bool isCavalry,
            bool isWallBreaker,
            float distToThreat,
            float distToSlot,
            RomanPhase phase)
        {
            if (Eq(doctrineId, "death-rush"))
                return false;
            if (isMissile || isCavalry || isWallBreaker || role == SquadRole.Flanker)
                return false;
            if (role != SquadRole.Front && role != SquadRole.Leader)
                return false;

            switch (order)
            {
                case DoctrineOrderKind.Advance:
                case DoctrineOrderKind.Charge:
                case DoctrineOrderKind.Flank:
                case DoctrineOrderKind.Kite:
                case DoctrineOrderKind.RetreatAndReform:
                case DoctrineOrderKind.FocusFire:
                    return false;
            }

            if (order != DoctrineOrderKind.Hold && order != DoctrineOrderKind.ProtectMissiles)
                return false;
            if (float.IsNaN(distToSlot) || distToSlot > FrontHoldSlotDist)
                return false;

            if (Eq(doctrineId, "roman") || Eq(doctrineId, "viking-shieldwall"))
            {
                return phase == RomanPhase.StandoffHold
                       || phase == RomanPhase.ContactHold
                       || phase == RomanPhase.RetreatPause;
            }

            if (Eq(doctrineId, "ambush"))
            {
                if (order != DoctrineOrderKind.Hold)
                    return false;
                if (float.IsNaN(distToThreat) || float.IsInfinity(distToThreat))
                    return false;
                var outer = PluginConfig.AmbushOuterPocket?.Value ?? AmbushDoctrine.OuterPocket;
                if (float.IsNaN(outer) || float.IsInfinity(outer) || outer < 2f)
                    outer = AmbushDoctrine.OuterPocket;
                return distToThreat <= outer;
            }

            if (float.IsNaN(distToThreat) || float.IsInfinity(distToThreat))
                return false;
            return distToThreat <= GenericContactBand;
        }

        /// <summary>
        /// Shift a formation centroid onto the standoff or swing line facing <paramref name="threat"/>.
        /// Zero when already inside that keep distance (don't walk backward off a good line).
        /// </summary>
        public static Vector3 ComputeAnchorShift(Vector3 centroid, Vector3 threat, float keepDistance)
        {
            var delta = threat - centroid;
            delta.y = 0f;
            var distSq = delta.x * delta.x + delta.z * delta.z;
            if (distSq < 0.0001f || float.IsNaN(keepDistance) || keepDistance < 0.5f)
                return Vector3.zero;
            var dist = (float)System.Math.Sqrt(distSq);
            if (dist <= keepDistance)
                return Vector3.zero;
            var inv = 1f / dist;
            var anchor = new Vector3(
                threat.x - delta.x * inv * keepDistance,
                centroid.y,
                threat.z - delta.z * inv * keepDistance);
            return anchor - centroid;
        }

        private static float KeepDistance(bool roman, RomanPhase phase, SquadRuntimeState? runtime)
        {
            if (phase == RomanPhase.PressContact)
            {
                if (runtime != null && runtime.ActiveSwingRange > 0.5f)
                    return runtime.ActiveSwingRange;
                return roman ? RomanDoctrine.DefaultSwingRange : VikingShieldWallDoctrine.DefaultSwingRange;
            }

            if (runtime != null && runtime.ActiveStandoffDistance > 0.5f)
                return runtime.ActiveStandoffDistance;
            return roman ? RomanDoctrine.StandoffDistance : VikingShieldWallDoctrine.StandoffDistance;
        }

        private static float ResolveThreatDistance(SquadUnit squad, SquadRuntimeState? runtime)
        {
            if (runtime != null && runtime.LastThreatDistance < float.MaxValue * 0.5f)
                return runtime.LastThreatDistance;
            if (squad.DebugNearestThreatDistance.HasValue)
                return squad.DebugNearestThreatDistance.Value;
            return runtime?.LastThreatDistance ?? float.MaxValue;
        }

        private static bool Eq(string? a, string b)
            => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        private static Vector3 ComputeCentroid(SquadUnit squad)
        {
            var sum = Vector3.zero;
            var n = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                sum += m.Position;
                n++;
            }
            return n > 0 ? new Vector3(sum.x / n, sum.y / n, sum.z / n) : Vector3.zero;
        }

        /// <summary>
        /// Build right/forward XZ basis from centroid → threat. Falls back to world +Z forward.
        /// ShieldWall lateral along right; depth along forward (missiles negative = behind).
        /// </summary>
        public static void BuildFacingBasis(Vector3 centroid, Vector3? threat, out Vector3 right, out Vector3 forward)
        {
            Vector3 dir;
            if (threat.HasValue)
            {
                dir = threat.Value - centroid;
                dir.y = 0f;
            }
            else
            {
                dir = new Vector3(0f, 0f, 1f);
            }

            var mag2 = dir.x * dir.x + dir.z * dir.z;
            if (mag2 < 0.0001f)
                dir = new Vector3(0f, 0f, 1f);
            else
            {
                var inv = 1f / (float)System.Math.Sqrt(mag2);
                dir = new Vector3(dir.x * inv, 0f, dir.z * inv);
            }

            forward = dir;
            // right = Cross(up, forward) so +right is to the formation's right facing threat
            right = new Vector3(forward.z, 0f, -forward.x);
            var r2 = right.x * right.x + right.z * right.z;
            if (r2 < 0.0001f)
                right = new Vector3(1f, 0f, 0f);
            else
            {
                var inv = 1f / (float)System.Math.Sqrt(r2);
                right = new Vector3(right.x * inv, 0f, right.z * inv);
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }

        private static Vector3 FormationSlot(
            FormationType formation,
            SquadRole role,
            int index,
            int count,
            Vector3 centroid,
            Vector3 current,
            bool cavalryFlanker,
            bool artilleryRear,
            Vector3 right,
            Vector3 forward)
        {
            float spacing = 2.2f;
            float lateral = (index - (count - 1) / 2f) * spacing;

            // Cavalry: wide orbit slots — do not join dense rank.
            if (cavalryFlanker)
            {
                var angle = (float)(index * (2 * System.Math.PI / System.Math.Max(1, count)));
                float radius = formation == FormationType.Orb ? 7f : 6f;
                return centroid + new Vector3(
                    (float)System.Math.Cos(angle) * radius,
                    0f,
                    (float)System.Math.Sin(angle) * radius);
            }

            switch (formation)
            {
                case FormationType.ShieldWall:
                case FormationType.Line:
                    {
                        // depth along forward: Front=0, Leader slightly back, Missile behind, Flanker offset
                        float depth = role == SquadRole.Missile || artilleryRear ? -5.5f
                            : role == SquadRole.Leader ? -1.5f
                            : role == SquadRole.Flanker ? (lateral < 0 ? -1f : 1f) * 2.5f
                            : 0f;
                        if (role == SquadRole.Flanker)
                            lateral *= 1.6f;
                        return centroid + right * lateral + forward * depth;
                    }
                case FormationType.Wedge:
                    {
                        float depth = -System.Math.Abs(lateral) * 0.6f;
                        return centroid + right * (lateral * 0.8f) + forward * depth;
                    }
                case FormationType.Skirmish:
                    {
                        float depth = artilleryRear ? -6f : (index % 2 == 0 ? 2f : -2f);
                        return centroid + right * (lateral * 1.4f) + forward * depth;
                    }
                case FormationType.Orb:
                    {
                        var angle = (float)(index * (2 * System.Math.PI / System.Math.Max(1, count)));
                        float radius = artilleryRear ? 6.5f : 4f;
                        return centroid + new Vector3(
                            (float)System.Math.Cos(angle) * radius,
                            0f,
                            (float)System.Math.Sin(angle) * radius);
                    }
                default:
                    return artilleryRear
                        ? centroid + right * lateral + forward * -5f
                        : current;
            }
        }

        /// <summary>
        /// Squad-level threat for formation facing: any member's combat target, else nearest player.
        /// </summary>
        private static Vector3? TryGetSquadThreatPosition(SquadUnit squad, Vector3 centroid)
        {
            foreach (var member in squad.Members)
            {
                if (!member.IsAlive)
                    continue;
                var t = TryGetThreatPosition(member, centroid);
                if (t.HasValue)
                    return t;
            }
            return null;
        }

        /// <summary>
        /// Charge / FocusFire approach point: combat target if known, else nearest player, else null (keep slot).
        /// </summary>
        private static Vector3? TryGetThreatPosition(SquadMemberView member, Vector3 centroid)
        {
#if VALHEIM_REFS
            try
            {
                if (member.NativeHandle is MonsterAI ai)
                {
                    var target = ai.GetTargetCreature();
                    if (target != null)
                        return target.transform.position;
                }
            }
            catch
            {
                // fall through to players
            }

            try
            {
                Vector3? best = null;
                float bestDist = float.MaxValue;
                var origin = member.Position;
                if (origin == Vector3.zero)
                    origin = centroid;
                // Dedicated: Character.IsPlayer / ZNet positions (not GetAllPlayers alone).
                foreach (var pos in FactionTactics.Util.ValheimWorldScan.CollectPlayerPositions())
                {
                    var d = Vector3.Distance(origin, pos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = pos;
                    }
                }

                return best;
            }
            catch
            {
                return null;
            }
#else
            _ = member;
            _ = centroid;
            return null;
#endif
        }

        private static bool Contains(string? name, string token)
            => name != null
               && name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
