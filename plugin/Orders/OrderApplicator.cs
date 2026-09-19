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
        /// <summary>Front/Leader snaps to HoldGround when within this of their slot (meters).</summary>
        public const float FrontHoldSlotDist = 2.5f;

        /// <summary>Last applied order keyed by MonsterAI instance id (or hash).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent> Intents
            = new System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent>();

        public void Apply(SquadUnit squad, SquadOrder order)
        {
            var centroid = ComputeCentroid(squad);
            var doctrineId = squad.Doctrine?.Id ?? "";
            var jelly = string.Equals(doctrineId, "artillery-jelly", System.StringComparison.OrdinalIgnoreCase);
            var charred = string.Equals(doctrineId, "charred-legion", System.StringComparison.OrdinalIgnoreCase);
            var roman = string.Equals(doctrineId, "roman", System.StringComparison.OrdinalIgnoreCase);
            var ambush = string.Equals(doctrineId, "ambush", System.StringComparison.OrdinalIgnoreCase);
            var index = 0;
            var count = squad.Members.Count;

            // Threat-facing basis for ShieldWall / Line (centroid → threat / nearest player).
            var threatPos = TryGetSquadThreatPosition(squad, centroid);
            BuildFacingBasis(centroid, threatPos, out var right, out var forward);

            foreach (var member in squad.Members)
            {
                if (!member.IsAlive)
                    continue;

                var isCavalry = charred && Contains(member.PrefabName, "Asksvin");
                var isArtillery = jelly
                    || Contains(member.PrefabName, "Gjall")
                    || (member.AssignedRole == SquadRole.Missile && jelly);

                var slot = FormationSlot(
                    order.Formation,
                    member.AssignedRole,
                    index,
                    count,
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

                // Cavalry / Asksvin: never HoldGround in the rank; always allow skirmish chase on Flank.
                // Missiles never HoldGround on Hold/ProtectMissiles — MoveTo rear slot + PreferKeepRange.
                var holdGround = !isCavalry
                    && !isMissile
                    && !isWallBreaker // wall-breakers must leave formation slots to press pieces
                    && (order.OrderKind == DoctrineOrderKind.Hold
                        || (order.OrderKind == DoctrineOrderKind.ProtectMissiles && !assaultMissileCover));

                // Missiles on FocusWallman may hold/skirmish rear while fronts breach.
                if (assaultMissileCover)
                    holdGround = false;

                // Ambush flankers never HoldGround — stay mobile on the orbit.
                if (ambush && member.AssignedRole == SquadRole.Flanker)
                    holdGround = false;

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

                // Roman (and ShieldWall/Line Front): do not chase on Hold/Advance/ProtectMissiles.
                // HoldGround when within FrontHoldSlotDist of slot; else MoveTo slot with AllowVanillaChase=false.
                var isFrontLine = member.AssignedRole == SquadRole.Front
                                  || member.AssignedRole == SquadRole.Leader;
                var lineHoldingOrder = order.OrderKind == DoctrineOrderKind.Hold
                                       || order.OrderKind == DoctrineOrderKind.Advance
                                       || order.OrderKind == DoctrineOrderKind.ProtectMissiles;
                var shieldOrLine = order.Formation == FormationType.ShieldWall
                                   || order.Formation == FormationType.Line;

                if (!isWallBreaker && !isCavalry && !isMissile && isFrontLine && (roman || shieldOrLine))
                {
                    // Front on Hold/ProtectMissiles: always HoldGround + formation slot facing threat.
                    if (order.OrderKind == DoctrineOrderKind.Hold
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles)
                    {
                        holdGround = true;
                        allowChase = false;
                    }
                    // Roman Advance/FocusFire: also pin Front to wall (StopMoving, no chase).
                    else if (roman && (order.OrderKind == DoctrineOrderKind.Advance
                                      || order.OrderKind == DoctrineOrderKind.FocusFire))
                    {
                        holdGround = true;
                        allowChase = false;
                    }
                    else if (lineHoldingOrder)
                    {
                        var distToSlot = HorizontalDistance(member.Position, slot);
                        holdGround = distToSlot <= FrontHoldSlotDist;
                        allowChase = false; // only Charge allows chase for Front on line doctrines
                    }
                }

                // DesiredPosition = formation slot (threat-facing basis) for Hold/ProtectMissiles Front.
                // Charge only: approach threat. FocusFire missiles may keep slot (PreferKeepRange).
                var desired = slot;
                var frontHoldSlot = isFrontLine
                    && !isMissile
                    && (order.OrderKind == DoctrineOrderKind.Hold
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles);
                if (frontHoldSlot)
                {
                    desired = slot; // explicit: formation slot facing threat
                    holdGround = true;
                    allowChase = false;
                }
                else if (!holdGround
                    && order.OrderKind == DoctrineOrderKind.Charge)
                {
                    var threat = TryGetThreatPosition(member, centroid);
                    if (threat.HasValue)
                        desired = threat.Value;
                }
                else if (!holdGround
                    && !keepRange
                    && order.OrderKind == DoctrineOrderKind.FocusFire
                    && !roman) // non-Roman FocusFire may close; Roman Front already HoldGround
                {
                    var threat = TryGetThreatPosition(member, centroid);
                    if (threat.HasValue)
                        desired = threat.Value;
                }

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
                                || isWallBreaker,
                    AllowVanillaChase = allowChase,
                    PreferKeepRange = keepRange,
                    AssaultWallBreaker = isWallBreaker,
                    AssaultMissileCover = assaultMissileCover,
                    AllowVanillaStructure = allowVanillaStructure,
                };

                Intents[member.InstanceId] = intent;
                index++;
            }
        }

        public static bool TryGetIntent(long instanceId, out MemberIntent intent)
            => Intents.TryGetValue(instanceId, out intent!);

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

    public sealed class MemberIntent
    {
        public string SquadId { get; set; } = "";
        public DoctrineOrderKind OrderKind { get; set; }
        public FormationType Formation { get; set; }
        public StanceType Stance { get; set; }
        public SquadRole Role { get; set; }
        public Vector3 DesiredPosition { get; set; }
        public long? FocusTargetId { get; set; }
        public bool HoldGround { get; set; }
        public bool PreferRun { get; set; }
        public bool AllowVanillaChase { get; set; }
        /// <summary>Artillery jelly / kite: suppress melee chase into danger.</summary>
        public bool PreferKeepRange { get; set; }

        /// <summary>Siege Assault: melee/front/brute pressing structure breach.</summary>
        public bool AssaultWallBreaker { get; set; }

        /// <summary>Siege Assault: missile covering wall-breakers (FocusWallman).</summary>
        public bool AssaultMissileCover { get; set; }

        /// <summary>Quiet assault: leave vanilla structure targeting alone.</summary>
        public bool AllowVanillaStructure { get; set; }
    }
}
