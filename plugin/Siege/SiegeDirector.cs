using System;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Squad;
using UnityEngine;

namespace FactionTactics.Siege
{
    /// <summary>
    /// Siege Assault v1 — Assault-only mode (no Defense / Raid Event yet).
    ///
    /// HARD INVARIANT: never spawn mobs, never start vanilla RandomEvents/raids,
    /// never raise encounter rate. Only retarget AI for squads that already exist
    /// (discovered living Greydwarf*/Draugr* near a workbench).
    ///
    /// Trigger: player workbenches / crafting stations within WorkbenchTriggerRange
    /// (VALHEIM_REFS Piece + CraftingStation heuristic; stub-safe → NearWorkbench=false).
    ///
    /// Eligible doctrines (v1): Ambush (Greydwarf / Black Forest), VikingShieldWall (Draugr / Swamp).
    /// Meadows: no siege. Higher biomes: siege flags not enabled yet.
    ///
    /// Building damage policy:
    ///   • No players near assault → light-touch Advance; do not fight vanilla structure targeting.
    ///   • Players present → role split (see <see cref="AssaultStance"/> /
    ///     OrderApplicator): wall-breakers press breach; missiles ProtectMissiles / FocusFire
    ///     covering those breachers.
    ///
    /// Order vocabulary: map siege intents onto existing DoctrineOrderKind (no new kinds):
    ///   Encircle      → Flank
    ///   TestBreach    → Charge (or Advance while closing)
    ///   FocusWallman  → FocusFire / ProtectMissiles (missile cover for wall-breakers)
    ///   Withdraw      → RetreatAndReform / Kite
    /// </summary>
    public sealed class SiegeDirector
    {
        /// <summary>Doctrine ids that may enter Assault stance in v1.</summary>
        public static bool IsSiegeEligibleDoctrine(string? doctrineId)
        {
            if (string.IsNullOrEmpty(doctrineId))
                return false;
            if (Eq(doctrineId!, "ambush"))
                return PluginConfig.EnableSiegeAmbush?.Value ?? true;
            if (Eq(doctrineId!, "viking-shieldwall"))
                return PluginConfig.EnableSiegeViking?.Value ?? true;
            return false;
        }

        public static bool IsMasterEnabled => PluginConfig.EnableSiegeAssault?.Value ?? true;

        public static float WorkbenchRange => PluginConfig.WorkbenchTriggerRange?.Value ?? 48f;

        public static int MinAssaultSquadSize => PluginConfig.SiegeMinSquadSize?.Value ?? 3;

        /// <summary>
        /// True when master + per-faction flags allow assault and the squad is large enough.
        /// NearWorkbench must already be set on the assessment/snapshot.
        /// </summary>
        public static bool ShouldEnterAssault(SquadSnapshot snapshot)
        {
            if (!IsMasterEnabled || snapshot == null)
                return false;
            if (!snapshot.NearWorkbench)
                return false;
            if (!IsSiegeEligibleDoctrine(snapshot.DoctrineId))
                return false;
            if (snapshot.MemberCount < MinAssaultSquadSize)
                return false;
            return true;
        }

        /// <summary>
        /// Fill workbench + player-near-assault heuristics on <paramref name="assessment"/>.
        /// Stub / CI (!VALHEIM_REFS): leaves flags false (Assault path still unit-testable via injection).
        /// </summary>
        public void EnrichWorkbenchProximity(Vector3 centroid, ThreatAssessment assessment)
        {
            if (assessment == null)
                return;

            if (!IsMasterEnabled)
            {
                assessment.NearWorkbench = false;
                assessment.NearestWorkbenchDistance = float.MaxValue;
                assessment.PlayersNearAssault = false;
                return;
            }

#if VALHEIM_REFS
            var range = WorkbenchRange;
            DetectWorkbench(centroid, range, assessment);
            if (assessment.NearWorkbench)
                DetectPlayersNearAssault(centroid, range, assessment);
            else
                assessment.PlayersNearAssault = false;
#else
            // Stub-safe: no game Piece/Player APIs. Doctrines/tests may set flags manually.
            _ = centroid;
            assessment.NearWorkbench = false;
            assessment.NearestWorkbenchDistance = float.MaxValue;
            assessment.PlayersNearAssault = false;
            _ = PluginConfig.WorkbenchTriggerRange;
#endif
        }

        /// <summary>
        /// Assault FSM. Returns null when assault is inactive — caller uses normal doctrine SelectOrder.
        /// </summary>
        public DoctrineOrderKind? TrySelectAssaultOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            if (!ShouldEnterAssault(snapshot))
                return null;

            return AssaultStance.SelectOrder(snapshot, previous);
        }

#if VALHEIM_REFS
        /// <summary>
        /// Heuristic: Piece name tokens + CraftingStation component when present.
        /// TODO(hypothesis): CraftingStation.m_range / Piece.m_craftingStation — verify in-game.
        /// </summary>
        private static void DetectWorkbench(Vector3 centroid, float range, ThreatAssessment assessment)
        {
            float nearest = float.MaxValue;
            bool found = false;
            try
            {
                var pieces = UnityEngine.Object.FindObjectsOfType<Piece>();
                foreach (var p in pieces)
                {
                    if (p == null)
                        continue;
                    var d = Vector3.Distance(centroid, p.transform.position);
                    if (d > range)
                        continue;
                    if (!LooksLikeWorkbench(p))
                        continue;
                    found = true;
                    if (d < nearest)
                        nearest = d;
                }
            }
            catch
            {
                // leave false — stub-tolerant
            }

            assessment.NearWorkbench = found;
            assessment.NearestWorkbenchDistance = nearest;
        }

        private static bool LooksLikeWorkbench(Piece piece)
        {
            var n = piece.name ?? "";
            if (Contains(n, "workbench")
                || Contains(n, "craftingstation")
                || Contains(n, "crafting_station")
                || Contains(n, "forge")
                || Contains(n, "stonecutter")
                || Contains(n, "artisans")
                || Contains(n, "cauldron")
                || Contains(n, "piece_workbench")
                || Contains(n, "piece_forge"))
                return true;

            try
            {
                // CraftingStation component is the authoritative player-station marker when available.
                var cs = piece.GetComponent("CraftingStation");
                if (cs != null)
                    return true;
                var field = typeof(Piece).GetField("m_craftingStation");
                if (field != null && field.GetValue(piece) != null)
                    return true;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        /// <summary>
        /// Players within assault bubble (workbench range around squad centroid).
        /// Drives role-split vs vanilla-structure light-touch.
        /// Dedicated: Character.IsPlayer / ZNet player list positions (not FindObjectsOfType).
        /// </summary>
        private static void DetectPlayersNearAssault(Vector3 centroid, float range, ThreatAssessment assessment)
        {
            try
            {
                foreach (var pos in FactionTactics.Util.ValheimWorldScan.CollectPlayerPositions())
                {
                    if (Vector3.Distance(centroid, pos) <= range)
                    {
                        assessment.PlayersNearAssault = true;
                        return;
                    }
                }
            }
            catch
            {
                // If player scan fails, treat as "players present" when ThreatCount already > 0
                // so we still role-split rather than leave breachers naked.
                assessment.PlayersNearAssault = assessment.ThreatCount > 0;
                return;
            }

            assessment.PlayersNearAssault = false;
        }

        private static bool Contains(string name, string token)
            => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
#endif

        private static bool Eq(string a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Assault-only FSM for Ambush + VikingShieldWall.
    /// Siege intents mapped onto shared DoctrineOrderKind (see SiegeDirector summary).
    /// </summary>
    public static class AssaultStance
    {
        const float AnxietyCasualtiesAmbush = 0.25f;
        const float AnxietyCasualtiesViking = 0.40f;
        const float BreachCommitRange = 14f;

        public static DoctrineOrderKind SelectOrder(SquadSnapshot snapshot, DoctrineOrderKind? previous)
        {
            if (snapshot == null)
                return DoctrineOrderKind.Hold;

            var ambush = Eq(snapshot.DoctrineId, "ambush");
            var anxiety = ambush ? AnxietyCasualtiesAmbush : AnxietyCasualtiesViking;

            // --- Withdraw (RetreatAndReform / Kite) ---
            if (snapshot.IsBroken || snapshot.CasualtyRatio >= anxiety)
                return DoctrineOrderKind.RetreatAndReform;

            if (previous == DoctrineOrderKind.Charge && ambush)
            {
                // Ambush flash-breach: refuse slugfest after a TestBreach charge.
                return DoctrineOrderKind.RetreatAndReform;
            }

            if (previous == DoctrineOrderKind.RetreatAndReform)
            {
                // Quiet workbench: resume light-touch Advance / AllowVanillaStructure — never
                // permanent Kite around an empty base (PlayersNearAssault already false).
                if (!snapshot.PlayersNearAssault)
                    return DoctrineOrderKind.Advance;

                // Hot withdraw → reform; re-enter Advance when gap opens.
                if (snapshot.NearestThreatDistance > snapshot.AdvanceRange
                    && snapshot.NearestWorkbenchDistance > WorkbenchComfortGap(snapshot))
                    return DoctrineOrderKind.Advance; // re-approach workbench / TestBreach close
                return ambush ? DoctrineOrderKind.Kite : DoctrineOrderKind.Advance;
            }

            // --- Quiet assault: no players near → light-touch; don't fight vanilla structure AI ---
            // Mapped: TestBreach approach → Advance. OrderApplicator sets AllowVanillaStructure.
            if (!snapshot.PlayersNearAssault)
                return DoctrineOrderKind.Advance;

            // --- Players present: role split (squad order + applicator per-role) ---
            // Missiles → FocusWallman cover (FocusFire / ProtectMissiles on players threatening breachers).
            // Wall-breakers (Front / Leader / melee Flanker) → TestBreach (Charge/Advance) / Encircle (Flank).
            var hasMissiles = snapshot.CountByRole(SquadRole.Missile) > 0;
            var hasBreakers = snapshot.CountByRole(SquadRole.Front) > 0
                              || snapshot.CountByRole(SquadRole.Leader) > 0
                              || snapshot.CountByRole(SquadRole.Flanker) > 0;

            // Prefer TestBreach / Encircle at squad level when breakers exist and workbench is
            // in commit range — do not let FocusWallman monopolize OrderKind and starve breach.
            // Applicator still keeps missiles on PreferKeepRange cover.
            var workbenchInCommit = snapshot.NearestWorkbenchDistance <= BreachCommitRange;
            if (hasBreakers && workbenchInCommit)
            {
                if (ambush && previous != DoctrineOrderKind.Flank && previous != DoctrineOrderKind.Charge)
                    return DoctrineOrderKind.Flank; // Encircle then TestBreach
                return DoctrineOrderKind.Charge;    // TestBreach
            }

            // Missile cover when not actively committing breach (or no breakers).
            if (hasMissiles && (snapshot.ThreatCount > 0 || snapshot.MissileThreatened
                                || snapshot.NearestThreatDistance <= snapshot.AdvanceRange))
            {
                if (previous == DoctrineOrderKind.FocusFire || snapshot.MissileThreatened)
                    return DoctrineOrderKind.ProtectMissiles; // FocusWallman — cover breachers
                return DoctrineOrderKind.FocusFire;          // FocusWallman — punish players on wallmen
            }

            // Closing on breach band.
            if (snapshot.NearestWorkbenchDistance > BreachCommitRange
                && snapshot.NearestThreatDistance > snapshot.ChargeRange)
            {
                // Encircle for Ambush swarm; Viking advances the wall toward the breach.
                if (ambush && hasBreakers)
                    return DoctrineOrderKind.Flank; // Encircle
                return DoctrineOrderKind.Advance;   // TestBreach approach
            }

            // Commit breach (fallback when workbench distance unknown / breakers closing).
            if (hasBreakers)
            {
                if (ambush && previous != DoctrineOrderKind.Flank && previous != DoctrineOrderKind.Charge)
                    return DoctrineOrderKind.Flank; // Encircle then TestBreach
                return DoctrineOrderKind.Charge;    // TestBreach
            }

            return DoctrineOrderKind.Advance;
        }

        private static float WorkbenchComfortGap(SquadSnapshot snapshot)
            => Math.Max(18f, snapshot.AdvanceRange * 0.75f);

        private static bool Eq(string? a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
