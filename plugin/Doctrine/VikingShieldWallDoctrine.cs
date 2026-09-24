using System;
using System.Collections.Generic;
using System.Linq;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Draugr* → Viking shield wall. Same cadence as Roman, tighter:
    /// standoff ~12–15m (indoors clamps toward 12), hold roll 1..8s, press to swing,
    /// retreat opens a 1s hold then press. Missiles keep range. No eternal choke Hold.
    /// </summary>
    public sealed class VikingShieldWallDoctrine : DoctrinePackBase
    {
        public const float StandoffDistance = 14f;
        public const float IndoorsStandoff = 12f;
        public const float DefaultSwingRange = 4.0f;
        public const float StandoffHoldMin = 1f;
        public const float StandoffHoldMax = 6f;
        public const float RetreatPauseSeconds = 1f;
        public override string Id => "viking-shieldwall";
        public override string DisplayName => "VikingShieldWall";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Draugr",
        };

        public override bool IsEnabled => PluginConfig.EnableVikingShieldWall?.Value ?? true;


        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            if (IsArcher(member) || member.LooksLikeMissile)
            {
                member.LooksLikeMissile = true;
                return SquadRole.Missile;
            }

            var hasLeader = squad.Any(m =>
                m.AssignedRole == SquadRole.Leader && m.InstanceId != member.InstanceId);
            if (!hasLeader && (member.LooksLikeLeader || IsElite(member) || IsLowestId(member, squad)))
            {
                member.LooksLikeHeavy = true;
                return SquadRole.Leader;
            }

            // Shield-wall body; light flankers only if clearly marked.
            if (!member.LooksLikeHeavy && member.LooksLikeFlanker)
            {
                var flankerCount = squad.Count(m => m.AssignedRole == SquadRole.Flanker);
                if (flankerCount < Math.Max(1, squad.Count / 5))
                    return SquadRole.Flanker;
            }

            member.LooksLikeHeavy = true;
            return SquadRole.Front;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            if (snapshot.ThreatCount <= 0)
            {
                BandedCadence.Reset(snapshot);
                return DoctrineOrderKind.Hold;
            }

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.40f)
                return DoctrineOrderKind.RetreatAndReform;

            _ = previous;
            var profile = LiveProfile(snapshot.IndoorsOrCrypt);
            BandedCadence.Step(snapshot, profile);
            var order = BandedCadence.OrderFor(snapshot.RomanPhase);

            // 1.0.12: inside swing band on ContactHold/PressContact → Charge (vanilla Attack).
            var d = BandedCadence.BandDistance(snapshot);
            var pressBand = profile.SwingRange * AggressionWeights.ForVikingDraugr();
            if ((snapshot.RomanPhase == RomanPhase.ContactHold
                 || snapshot.RomanPhase == RomanPhase.PressContact)
                && d <= pressBand
                && !snapshot.IsBroken)
                return DoctrineOrderKind.Charge;

            return order;
        }

        /// <summary>Open field uses <see cref="StandoffDistance"/>; crypts clamp toward <see cref="IndoorsStandoff"/>.</summary>
        public static CadenceProfile LiveProfile(bool indoors)
        {
            var open = PluginConfig.VikingStandoffDistance?.Value ?? StandoffDistance;
            var choke = PluginConfig.VikingIndoorsStandoff?.Value ?? IndoorsStandoff;
            var standoff = indoors ? Math.Min(open, choke) : open;
            var swing = PluginConfig.VikingContactSwingRange?.Value ?? DefaultSwingRange;
            var min = PluginConfig.VikingStandoffHoldMin?.Value ?? StandoffHoldMin;
            var max = PluginConfig.VikingStandoffHoldMax?.Value ?? StandoffHoldMax;
            // 1.0.12 draugr overall aggression: shorten standoff hold (score/weight cadence, not spawns).
            max = Math.Max(min, max / AggressionWeights.VikingDraugr);
            var pause = PluginConfig.VikingRetreatPauseSeconds?.Value ?? RetreatPauseSeconds;
            return CadenceProfile.Resolve(standoff, swing, min, max, pause);
        }

        public static bool IsArcher(SquadMemberView member)
            => Contains(member.PrefabName, "Archer")
               || Contains(member.PrefabName, "Bow")
               || Contains(member.PrefabName, "Ranged");

        public static bool IsElite(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Captain")
               || Contains(member.PrefabName, "Lord");


    }
}
