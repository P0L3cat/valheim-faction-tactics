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
    /// </summary>
    public sealed class OrderApplicator
    {
        /// <summary>Last applied order keyed by MonsterAI instance id (or hash).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent> Intents
            = new System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent>();

        public void Apply(SquadUnit squad, SquadOrder order)
        {
            var centroid = ComputeCentroid(squad);
            var doctrineId = squad.Doctrine?.Id ?? "";
            var jelly = string.Equals(doctrineId, "artillery-jelly", System.StringComparison.OrdinalIgnoreCase);
            var charred = string.Equals(doctrineId, "charred-legion", System.StringComparison.OrdinalIgnoreCase);
            var index = 0;
            var count = squad.Members.Count;

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
                    isArtillery || jelly);

                var keepRange = jelly
                    || order.OrderKind == DoctrineOrderKind.Kite
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

                // Cavalry / Asksvin: never HoldGround in the rank; always allow skirmish chase on Flank.
                var holdGround = !isCavalry
                    && !isWallBreaker // wall-breakers must leave formation slots to press pieces
                    && (order.OrderKind == DoctrineOrderKind.Hold
                        || (order.OrderKind == DoctrineOrderKind.ProtectMissiles && !assaultMissileCover));

                // Missiles on FocusWallman may hold/skirmish rear while fronts breach.
                if (assaultMissileCover)
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

                var intent = new MemberIntent
                {
                    SquadId = squad.SquadId,
                    OrderKind = order.OrderKind,
                    Formation = order.Formation,
                    Stance = order.Stance,
                    Role = member.AssignedRole,
                    DesiredPosition = slot,
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

        private static Vector3 FormationSlot(
            FormationType formation,
            SquadRole role,
            int index,
            int count,
            Vector3 centroid,
            Vector3 current,
            bool cavalryFlanker,
            bool artilleryRear)
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
                        float depth = role == SquadRole.Missile || artilleryRear ? -5.5f
                            : role == SquadRole.Leader ? -1.5f
                            : role == SquadRole.Flanker ? (lateral < 0 ? -1f : 1f) * 2.5f
                            : 0f;
                        if (role == SquadRole.Flanker)
                            lateral *= 1.6f;
                        return centroid + new Vector3(lateral, 0f, depth);
                    }
                case FormationType.Wedge:
                    {
                        float depth = -System.Math.Abs(lateral) * 0.6f;
                        return centroid + new Vector3(lateral * 0.8f, 0f, depth);
                    }
                case FormationType.Skirmish:
                    {
                        float depth = artilleryRear ? -6f : (index % 2 == 0 ? 2f : -2f);
                        return centroid + new Vector3(lateral * 1.4f, 0f, depth);
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
                        ? centroid + new Vector3(lateral, 0f, -5f)
                        : current;
            }
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
