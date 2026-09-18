using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Blob* artillery jelly: keep range, zone denial, avoid melee-chase into fire.
    /// </summary>
    public sealed class ArtilleryJellyDoctrine : DoctrinePackBase
    {
        public override string Id => "artillery-jelly";
        public override string DisplayName => "ArtilleryJelly";

        public override IReadOnlyList<string> PrefabPrefixes { get; } = new[]
        {
            "Blob",
        };

        public override bool IsEnabled => PluginConfig.EnableArtilleryJelly?.Value ?? true;


        public override SquadRole AssignRole(SquadMemberView member, IReadOnlyList<SquadMemberView> squad)
        {
            // Blobs are zone-denial artillery — always Missile so PreferKeepRange rear slots
            // stay intact. Encode alpha via LooksLikeLeader only (never SquadRole.Leader).
            member.LooksLikeMissile = true;
            member.LooksLikeLeader = IsElite(member) || IsLowestId(member, squad);
            return SquadRole.Missile;
        }

        public override DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            // Artillery jelly FSM:
            // Hold/FocusFire at standoff → Kite if pressed → never Charge into melee/fire.
            // RetreatAndReform if broken; PreferKeepRange applicator flag suppresses chase.
            const float comfortMin = 10f;
            const float comfortMax = 22f;

            if (snapshot.ThreatCount <= 0)
                return DoctrineOrderKind.Hold;

            if (snapshot.IsBroken || snapshot.CasualtyRatio >= 0.45f)
                return DoctrineOrderKind.RetreatAndReform;

            // Never melee-chase — if somehow Charging, peel immediately.
            if (previous == DoctrineOrderKind.Charge)
                return DoctrineOrderKind.Kite;

            var d = snapshot.NearestThreatDistance;

            // Too close: kite / reform — avoid fire and melee.
            if (d < comfortMin)
            {
                if (previous == DoctrineOrderKind.Kite)
                    return DoctrineOrderKind.RetreatAndReform;
                return DoctrineOrderKind.Kite;
            }

            // Comfort band: zone denial FocusFire.
            if (d <= comfortMax)
                return DoctrineOrderKind.FocusFire;

            // Far: Advance slowly into artillery band (not Charge).
            return DoctrineOrderKind.Advance;
        }

        public static bool IsElite(SquadMemberView member)
            => Contains(member.PrefabName, "Elite")
               || Contains(member.PrefabName, "Oozer")
               || member.LooksLikeHeavy;


    }
}
