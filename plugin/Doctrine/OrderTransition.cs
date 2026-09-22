using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Phase B: Roman/Ambush pick a max score and only leave the current order when the
    /// leader clears <see cref="Hysteresis"/>. Hard vetoes (score &lt; 0) stay vetoes so an
    /// <see cref="IActionScorer"/> cannot resurrect an illegal Charge/Kite.
    /// Min dwell is applied by <see cref="SquadDirector"/> for every doctrine.
    /// </summary>
    public static class OrderScoreContext
    {
        [ThreadStatic]
        public static IActionScorer? ActionScorer;
    }

    public static class OrderTransition
    {
        public const float DefaultMinDwellSeconds = 1.25f;
        public const float DefaultHysteresis = 0.15f;

        /// <summary>Meters around a range edge where competing scores are pulled together.</summary>
        public const float EdgeBandMeters = 1.25f;

        public static float Hysteresis
        {
            get
            {
                var v = PluginConfig.OrderScoreHysteresis?.Value ?? DefaultHysteresis;
                if (float.IsNaN(v) || v < 0f)
                    return 0f;
                return v;
            }
        }

        public static float MinDwellSeconds
        {
            get
            {
                var v = PluginConfig.OrderMinDwellSeconds?.Value ?? DefaultMinDwellSeconds;
                if (float.IsNaN(v) || v < 0f)
                    return 0f;
                return v;
            }
        }

        public static Dictionary<DoctrineOrderKind, float> Blank(float floor = -8f)
        {
            var scores = new Dictionary<DoctrineOrderKind, float>();
            foreach (DoctrineOrderKind kind in Enum.GetValues(typeof(DoctrineOrderKind)))
                scores[kind] = floor;
            return scores;
        }

        /// <summary>
        /// Add the active action scorer onto legal utilities only. Vetoed orders (score &lt; 0)
        /// are doctrine identity — early Roman Charge, Roman Kite, Ambush blob Charge.
        /// </summary>
        public static void FoldActionScorer(
            Dictionary<DoctrineOrderKind, float> scores,
            SquadSnapshot snapshot)
        {
            var scorer = OrderScoreContext.ActionScorer;
            if (scorer == null || snapshot == null || scores == null)
                return;

            foreach (DoctrineOrderKind kind in Enum.GetValues(typeof(DoctrineOrderKind)))
            {
                if (!scores.TryGetValue(kind, out var utility) || utility < 0f)
                    continue;
                scores[kind] = utility + scorer.ScoreAction(kind, snapshot);
            }
        }

        public static DoctrineOrderKind Pick(
            IReadOnlyDictionary<DoctrineOrderKind, float> scores,
            DoctrineOrderKind? previous,
            float? hysteresis = null)
        {
            DoctrineOrderKind best = previous ?? DoctrineOrderKind.Hold;
            var bestScore = float.NegativeInfinity;
            var any = false;
            foreach (DoctrineOrderKind kind in Enum.GetValues(typeof(DoctrineOrderKind)))
            {
                if (scores == null || !scores.TryGetValue(kind, out var score))
                    continue;
                any = true;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = kind;
                }
            }

            if (!any)
                return previous ?? DoctrineOrderKind.Hold;
            if (previous == null)
                return best;

            var current = scores != null && scores.TryGetValue(previous.Value, out var currentScore)
                ? currentScore
                : float.NegativeInfinity;
            var margin = hysteresis ?? Hysteresis;
            if (float.IsNaN(margin) || margin < 0f)
                margin = 0f;

            if (best != previous.Value && bestScore > current + margin)
                return best;
            return previous.Value;
        }

        /// <summary>
        /// Pull <paramref name="a"/> and <paramref name="b"/> toward their average when
        /// <paramref name="distance"/> is inside <paramref name="bandMeters"/> of <paramref name="edgeMeters"/>.
        /// Quadratic so a few tenths of a meter sticks, and ~1m still clears default hysteresis.
        /// </summary>
        public static void SoftenEdge(
            Dictionary<DoctrineOrderKind, float> scores,
            DoctrineOrderKind a,
            DoctrineOrderKind b,
            float distance,
            float edgeMeters,
            float bandMeters = EdgeBandMeters)
        {
            if (scores == null || bandMeters <= 0.01f)
                return;
            if (!scores.TryGetValue(a, out var sa) || !scores.TryGetValue(b, out var sb))
                return;
            var dist = Math.Abs(distance - edgeMeters);
            if (dist >= bandMeters)
                return;
            var keep = dist / bandMeters;
            keep *= keep;
            var avg = (sa + sb) * 0.5f;
            scores[a] = avg + (sa - avg) * keep;
            scores[b] = avg + (sb - avg) * keep;
        }

        public static bool IsExplicitForce(
            string? doctrineId,
            DoctrineOrderKind? previous,
            DoctrineOrderKind next)
        {
            return Eq(doctrineId, "ambush")
                   && previous == DoctrineOrderKind.Charge
                   && next == DoctrineOrderKind.Kite;
        }

        /// <summary>
        /// Threat lost, broken (or Roman-shattered casualty retreat/kite), or Ambush Charge→Kite.
        /// </summary>
        public static bool BypassesMinDwell(
            string? doctrineId,
            SquadSnapshot snapshot,
            DoctrineOrderKind? previous,
            DoctrineOrderKind next)
        {
            if (snapshot == null)
                return false;
            if (previous == null || next == previous.Value)
                return true;
            if (snapshot.ThreatCount <= 0)
                return true;
            if (snapshot.IsBroken)
                return true;
            if (snapshot.CasualtyRatio >= 0.45f
                && (next == DoctrineOrderKind.RetreatAndReform || next == DoctrineOrderKind.Kite))
                return true;
            return IsExplicitForce(doctrineId, previous, next);
        }

        public static DoctrineOrderKind ApplyMinDwell(
            string? doctrineId,
            SquadSnapshot snapshot,
            DoctrineOrderKind? previous,
            DoctrineOrderKind proposed,
            float orderAgeSeconds,
            float? minDwellSeconds = null)
        {
            if (previous == null || proposed == previous.Value)
                return proposed;
            if (BypassesMinDwell(doctrineId, snapshot, previous, proposed))
                return proposed;

            var dwell = minDwellSeconds ?? MinDwellSeconds;
            if (float.IsNaN(dwell) || dwell < 0f)
                dwell = 0f;
            if (orderAgeSeconds >= dwell)
                return proposed;
            return previous.Value;
        }

        /// <summary>DeathRush never leaves Charge while a threat exists. Idle (no threat) may Hold.</summary>
        public static DoctrineOrderKind EnforceDeathRush(
            string? doctrineId,
            SquadSnapshot snapshot,
            DoctrineOrderKind order)
        {
            if (!Eq(doctrineId, "death-rush") || snapshot == null)
                return order;
            if (snapshot.ThreatCount <= 0)
                return order;
            return DoctrineOrderKind.Charge;
        }

        private static bool Eq(string? a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
