using FactionTactics.Doctrine;
using FactionTactics.Squad;
using UnityEngine;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Translates <see cref="SquadOrder"/> into per-member intent consumed by Harmony patches.
    /// </summary>
    public sealed class OrderApplicator
    {
        /// <summary>Last applied order keyed by MonsterAI instance id (or hash).</summary>
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent> Intents
            = new System.Collections.Concurrent.ConcurrentDictionary<long, MemberIntent>();

        public void Apply(SquadUnit squad, SquadOrder order)
        {
            var centroid = ComputeCentroid(squad);
            var index = 0;
            var count = squad.Members.Count;

            foreach (var member in squad.Members)
            {
                if (!member.IsAlive)
                    continue;

                var slot = FormationSlot(order.Formation, member.AssignedRole, index, count, centroid, member.Position);
                var intent = new MemberIntent
                {
                    SquadId = squad.SquadId,
                    OrderKind = order.OrderKind,
                    Formation = order.Formation,
                    Stance = order.Stance,
                    Role = member.AssignedRole,
                    DesiredPosition = slot,
                    FocusTargetId = order.FocusTargetId,
                    HoldGround = order.OrderKind == DoctrineOrderKind.Hold
                                 || order.OrderKind == DoctrineOrderKind.ProtectMissiles,
                    PreferRun = order.OrderKind == DoctrineOrderKind.Charge
                                || order.OrderKind == DoctrineOrderKind.RetreatAndReform
                                || order.OrderKind == DoctrineOrderKind.Kite,
                    AllowVanillaChase = order.OrderKind == DoctrineOrderKind.Charge
                                        || order.OrderKind == DoctrineOrderKind.FocusFire,
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
            Vector3 current)
        {
            // Lightweight geometric slots — Harmony layer moves toward DesiredPosition.
            float spacing = 2.2f;
            float lateral = (index - (count - 1) / 2f) * spacing;

            switch (formation)
            {
                case FormationType.ShieldWall:
                case FormationType.Line:
                    {
                        float depth = role == SquadRole.Missile ? -4f
                            : role == SquadRole.Leader ? -1.5f
                            : role == SquadRole.Flanker ? (lateral < 0 ? -1f : 1f)
                            : 0f;
                        return centroid + new Vector3(lateral, 0f, depth);
                    }
                case FormationType.Wedge:
                    {
                        float depth = -System.Math.Abs(lateral) * 0.6f;
                        return centroid + new Vector3(lateral * 0.8f, 0f, depth);
                    }
                case FormationType.Skirmish:
                    return centroid + new Vector3(lateral * 1.4f, 0f, (index % 2 == 0 ? 2f : -2f));
                case FormationType.Orb:
                    {
                        var angle = (float)(index * (2 * System.Math.PI / System.Math.Max(1, count)));
                        return centroid + new Vector3(
                            (float)System.Math.Cos(angle) * 4f,
                            0f,
                            (float)System.Math.Sin(angle) * 4f);
                    }
                default:
                    return current;
            }
        }
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
    }
}
