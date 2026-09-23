using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FactionTactics.Tests.Sim
{
    /// <summary>
    /// Azog adversarial geometric predicates over PlayerPathSim trajectories (XZ world).
    /// Primary win conditions for offline combat claims. Flags are never enough alone.
    /// Convention: P(t) player/sticky, M_i(t) member position, D_i(t) Desired, Dist, Delta.
    /// Hole-pick H1–H8: hard AND gates; soft OR-escapes and PackCentroid-as-slot removed.
    /// </summary>
    internal static class MotionPredicates
    {
        public const float DefaultWarmupFraction = 0.25f;
        public const float PiOverTwo = 1.5707963f;

        public static Vector3 Xz(Vector3 v) => new Vector3(v.x, 0f, v.z);

        public static float Dist(Vector3 a, Vector3 b)
        {
            var d = Xz(a) - Xz(b);
            return d.magnitude;
        }

        public static Vector3 Delta(Vector3 from, Vector3 to) => Xz(to) - Xz(from);

        public static IReadOnlyList<TickMetrics> AfterWarmup(IReadOnlyList<TickMetrics> hist, int warmupTicks)
        {
            if (hist == null || hist.Count == 0)
                return Array.Empty<TickMetrics>();
            var w = Math.Max(0, Math.Min(warmupTicks, hist.Count - 1));
            return hist.Skip(w).ToList();
        }

        public static int DefaultWarmup(int n, float fraction = DefaultWarmupFraction)
            => Math.Max(1, (int)(n * fraction));

        public static float PathLength(IReadOnlyList<Vector3> path)
        {
            float len = 0f;
            for (int i = 1; i < path.Count; i++)
                len += Dist(path[i - 1], path[i]);
            return len;
        }

        public static float PlayerPathLength(IReadOnlyList<TickMetrics> hist, long? stickyId = null)
        {
            var pts = new List<Vector3>();
            foreach (var h in hist)
            {
                if (stickyId.HasValue && h.HasSticky && h.StickyId == stickyId.Value)
                {
                    pts.Add(h.StickyPosition);
                    continue;
                }
                if (h.PlayerPositions.Count > 0)
                    pts.Add(h.PlayerPositions[0].pos);
                else if (h.HasSticky)
                    pts.Add(h.StickyPosition);
            }
            return PathLength(pts);
        }

        /// <summary>
        /// Fixed lattice slot from initial pack excluding straggler — NOT PackCentroid that walks with Ms.
        /// </summary>
        public static Vector3 FixedLatticeSlotFromPack(
            IReadOnlyList<Vector3> corePositions,
            int slotIndex,
            int capacity,
            Vector3 forward,
            float spacing = 2.2f)
        {
            if (corePositions == null || corePositions.Count == 0)
                return Vector3.zero;
            var c = Vector3.zero;
            foreach (var p in corePositions)
                c += Xz(p);
            c /= corePositions.Count;
            var f = Xz(forward);
            if (f.sqrMagnitude < 1e-6f)
                f = new Vector3(1f, 0f, 0f);
            else
                f = f.normalized;
            var right = new Vector3(f.z, 0f, -f.x);
            float lateral = (slotIndex - (capacity - 1) / 2f) * spacing;
            return c + right * lateral; // ShieldWall Front depth 0
        }

        /// <summary>
        /// (1) PreferRun/flee: threat T inject Dist(M,T)&lt;R_threat; radial outward escape.
        /// H6: cos≥0.707; full vMin*dt floor; Desired cos≥0.5; net Dist open ≥2; N≥8 after threaten.
        /// </summary>
        public static string? FleeRadialOutward(
            IReadOnlyList<TickMetrics> hist,
            Func<TickMetrics, Vector3> threatAt,
            long memberId,
            float rThreat,
            float cosThetaMin = 0.707f,
            float vMin = 2.0f,
            float dt = 0.25f,
            float deltaEscape = 2.0f,
            float desiredCosMin = 0.5f,
            int minTicksAfterThreaten = 8)
        {
            if (hist == null || hist.Count < minTicksAfterThreaten)
                return $"flee: need N≥{minTicksAfterThreaten}, got {hist?.Count ?? 0}";

            // H6: N≥8 after first threaten — take the next minTicksAfterThreaten samples (no early OR break).
            var escape = new List<TickMetrics>();
            float? dStart = null;
            foreach (var h in hist)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == memberId && x.HasIntent);
                if (!m.HasIntent) continue;
                var d = Dist(m.Position, threatAt(h));
                if (dStart == null)
                {
                    if (d >= rThreat)
                        continue;
                    dStart = d;
                }
                escape.Add(h);
                if (escape.Count >= minTicksAfterThreaten)
                    break;
            }

            if (escape.Count < minTicksAfterThreaten || dStart == null)
                return $"flee: Dist(M,T)<{rThreat} escape window N={escape.Count} < {minTicksAfterThreaten}";

            float sumCos = 0f;
            float sumStep = 0f;
            int nStep = 0;
            float sumDesCos = 0f;
            int nDes = 0;

            for (int i = 1; i < escape.Count; i++)
            {
                var a = escape[i - 1].Members.First(x => x.InstanceId == memberId);
                var b = escape[i].Members.First(x => x.InstanceId == memberId);
                var t = threatAt(escape[i - 1]);
                var radial = Xz(a.Position) - Xz(t);
                var step = Delta(a.Position, b.Position);
                var stepLen = step.magnitude;
                if (stepLen < 1e-5f)
                    continue;
                nStep++;
                sumStep += stepLen;
                if (radial.sqrMagnitude > 1e-6f)
                    sumCos += Vector3.Dot(radial.normalized, step / stepLen);

                var des = Xz(a.DesiredPosition) - Xz(a.Position);
                if (des.sqrMagnitude > 1e-6f && radial.sqrMagnitude > 1e-6f)
                {
                    nDes++;
                    sumDesCos += Vector3.Dot(radial.normalized, des.normalized);
                }
            }

            if (nStep < 2)
                return $"flee: parked/jitter — only {nStep} moving steps in escape window";

            var meanCos = sumCos / nStep;
            if (meanCos < cosThetaMin)
                return $"flee: mean radial cos={meanCos:F3} < {cosThetaMin:F3} (toward threat / tangential only)";

            var meanStep = sumStep / nStep;
            var minStep = vMin * dt; // H6: full vMin*dt floor (no half soft)
            if (meanStep < minStep)
                return $"flee: mean||Delta||={meanStep:F3} < full vMin*dt={minStep:F3}";

            var first = escape[0].Members.First(x => x.InstanceId == memberId);
            var last = escape[escape.Count - 1].Members.First(x => x.InstanceId == memberId);
            var d0 = Dist(first.Position, threatAt(escape[0]));
            var d1 = Dist(last.Position, threatAt(escape[escape.Count - 1]));
            if (d1 + 1e-3f < d0 + deltaEscape)
                return $"flee: Dist(end,T)={d1:F2} < Dist(start,T)+Δ={d0 + deltaEscape:F2}";

            if (nDes == 0)
                return "flee: no Desired samples for radial cos";
            var meanDesCos = sumDesCos / nDes;
            if (meanDesCos < desiredCosMin)
                return $"flee: Desired mean radial cos={meanDesCos:F3} < {desiredCosMin:F3}";

            return null;
        }

        /// <summary>
        /// (2) Straggler merge: Dist(Ms,C) from ≥R_out to ≤R_in (~slot radius 4–8);
        /// monotonic median Dist; pack coherence last K. PreferRun is NOT a geom claim.
        /// </summary>
        public static string? StragglerMerge(
            IReadOnlyList<TickMetrics> hist,
            long stragglerId,
            float rOut,
            float rIn,
            float rPack,
            int lastK = 4,
            int minTicks = 10)
        {
            if (hist == null || hist.Count < minTicks)
                return $"merge: need N≥{minTicks}, got {hist?.Count ?? 0}";
            if (rIn > 8.01f)
                return $"merge: harness fail — R_in={rIn} must be ≤8 (slot radius, not soft close)";

            var first = hist[0].Members.FirstOrDefault(m => m.InstanceId == stragglerId && m.HasIntent);
            var last = hist[hist.Count - 1].Members.FirstOrDefault(m => m.InstanceId == stragglerId && m.HasIntent);
            if (!first.HasIntent || !last.HasIntent)
                return "merge: straggler missing intent at start/end";

            // Centroid excluding Ms so C does not walk with the straggler.
            Vector3 CoreCentroid(TickMetrics h)
            {
                var sum = Vector3.zero;
                int n = 0;
                foreach (var m in h.Members.Where(x => x.HasIntent && x.InstanceId != stragglerId))
                {
                    sum += Xz(m.Position);
                    n++;
                }
                return n == 0 ? h.PackCentroid : sum / n;
            }

            var dists = new List<float>();
            foreach (var h in hist)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == stragglerId && x.HasIntent);
                if (!m.HasIntent) continue;
                dists.Add(Dist(m.Position, CoreCentroid(h)));
            }
            if (dists.Count < minTicks)
                return $"merge: sparse Dist samples {dists.Count}";

            var d0 = dists[0];
            var d1 = dists[dists.Count - 1];
            if (d0 < rOut)
                return $"merge: start Dist(Ms,C)={d0:F1} < R_out={rOut} (not a real straggler)";
            if (d1 > rIn)
                return $"merge: end Dist(Ms,C)={d1:F1} > R_in={rIn} (Ms did not close to slot)";

            var moved = Dist(first.Position, last.Position);
            var c0 = CoreCentroid(hist[0]);
            var towardCore = Dist(first.Position, c0) - Dist(last.Position, c0);
            if (moved < 2f && towardCore < (d0 - d1) * 0.25f)
                return $"merge: Position nearly frozen moved={moved:F1} towardCore={towardCore:F1} gap {d0:F1}→{d1:F1}";

            // Monotonic median: windowed medians must not rise overall.
            int win = Math.Max(3, dists.Count / 5);
            var medians = new List<float>();
            for (int i = 0; i + win <= dists.Count; i += Math.Max(1, win / 2))
            {
                var slice = dists.Skip(i).Take(win).OrderBy(x => x).ToList();
                medians.Add(slice[slice.Count / 2]);
            }
            if (medians.Count >= 2)
            {
                int rises = 0;
                for (int i = 1; i < medians.Count; i++)
                {
                    if (medians[i] > medians[i - 1] + 0.75f)
                        rises++;
                }
                if (rises > medians.Count / 3)
                    return $"merge: Dist(Ms,C) median not monotonic (rises={rises}/{medians.Count})";
                if (medians[medians.Count - 1] > medians[0] + 1f)
                    return $"merge: median Dist rose {medians[0]:F1}→{medians[medians.Count - 1]:F1}";
            }

            var tail = hist.Skip(Math.Max(0, hist.Count - lastK)).ToList();
            foreach (var h in tail)
            {
                var c = CoreCentroid(h);
                foreach (var m in h.Members.Where(x => x.HasIntent && x.InstanceId != stragglerId))
                {
                    var d = Dist(m.Position, c);
                    if (d > rPack)
                        return $"merge: non-straggler {m.InstanceId} Dist(C)={d:F1} > R_pack={rPack} on late tick {h.TickIndex}";
                }
            }

            var des0 = Dist(first.DesiredPosition, CoreCentroid(hist[0]));
            var des1 = Dist(last.DesiredPosition, CoreCentroid(hist[hist.Count - 1]));
            if (des1 > des0 + 1f && des1 > rIn + 5f)
                return $"merge: Desired drifted away from core {des0:F1}→{des1:F1}";

            return null;
        }

        /// <summary>
        /// Unique-id absorb once via MergeStragglers (supporting Fact for H5).
        /// </summary>
        public static string? MergeStragglersUniqueAbsorbOnce(
            IReadOnlyList<long> beforeParentIds,
            IReadOnlyList<long> absorbedIds,
            IReadOnlyList<long> afterParentIds)
        {
            if (absorbedIds == null || absorbedIds.Count == 0)
                return "merge-absorb: no absorbed ids";
            var after = afterParentIds ?? Array.Empty<long>();
            if (after.Count != after.Distinct().Count())
                return "merge-absorb: duplicate ids in parent after MergeStragglers";
            foreach (var id in absorbedIds)
            {
                if (!after.Contains(id))
                    return $"merge-absorb: id {id} not in parent after merge";
                if ((beforeParentIds ?? Array.Empty<long>()).Contains(id))
                    return $"merge-absorb: id {id} was already in parent (not a fresh absorb)";
            }
            return null;
        }

        /// <summary>
        /// (3) Ambush sticky orbit AND: flaps≤F_max, sticky held, Dist(M,P)∈[R_lo,R_hi] ≥80%,
        /// |Δφ|sum ≥ phiMin (π/2). H1: DROP angVar OR-escape; optional angVarAndMin as AND only.
        /// </summary>
        public static string? AmbushStickyOrbit(
            IReadOnlyList<TickMetrics> hist,
            float rLo = 8f,
            float rHi = 14f,
            float bandFraction = 0.80f,
            int flapsMax = 0,
            float phiMin = PiOverTwo,
            float? angVarAndMin = null,
            float playerPathMin = 6f,
            int minTicks = 16,
            int warmup = 4)
        {
            if (hist == null || hist.Count < minTicks)
                return $"orbit: need N≥{minTicks}, got {hist?.Count ?? 0}";

            var flaps = PlayerPathSim.CountStickyFlaps(hist);
            if (flaps > flapsMax)
                return $"orbit: sticky flaps={flaps} > F_max={flapsMax}";

            if (!hist.All(h => h.HasSticky))
                return "orbit: sticky not held every tick";

            var stickyId = hist[0].StickyId;
            if (!hist.All(h => h.StickyId == stickyId))
                return "orbit: sticky id changed without counting as flap budget";

            var pathLen = PlayerPathLength(hist, stickyId);
            if (pathLen < playerPathMin)
                return $"orbit: player path length {pathLen:F1}m < L_min={playerPathMin}";

            // H1: |Δφ| over FULL hist (fan-into-orbit); Dist band after warmup ≥80%.
            var prevPhi = new Dictionary<long, float>();
            float sumAbsDPhi = 0f;
            var angles = new List<float>();
            foreach (var h in hist)
            {
                var p = h.StickyPosition;
                foreach (var m in h.Members.Where(x => x.HasIntent))
                {
                    var off = Xz(m.Position) - Xz(p);
                    if (off.sqrMagnitude <= 0.25f)
                        continue;
                    var phi = Mathf.Atan2(off.z, off.x);
                    angles.Add(phi);
                    if (prevPhi.TryGetValue(m.InstanceId, out var prev))
                    {
                        var dphi = phi - prev;
                        while (dphi > Math.PI) dphi -= (float)(2 * Math.PI);
                        while (dphi < -Math.PI) dphi += (float)(2 * Math.PI);
                        sumAbsDPhi += Math.Abs(dphi);
                    }
                    prevPhi[m.InstanceId] = phi;
                }
            }

            var steady = AfterWarmup(hist, warmup);
            int inBand = 0;
            int samples = 0;
            foreach (var h in steady)
            {
                var p = h.StickyPosition;
                foreach (var m in h.Members.Where(x => x.HasIntent))
                {
                    var d = Dist(m.Position, p);
                    samples++;
                    if (d >= rLo && d <= rHi)
                        inBand++;
                }
            }

            if (samples == 0)
                return "orbit: no member samples";
            var frac = (float)inBand / samples;
            if (frac < bandFraction)
                return $"orbit: Dist(M,P) in [{rLo},{rHi}] only {frac:P0} (<{bandFraction:P0})";

            // H1: |Δφ|sum REQUIRED (AND with band). angVar alone must not pass.
            if (sumAbsDPhi < phiMin)
                return $"orbit: |Δφ|sum={sumAbsDPhi:F3} < {phiMin} (angVar OR-escape removed; freeze-ring must fail)";

            float varAng = 0f;
            if (angles.Count >= 3)
            {
                var meanAng = angles.Average();
                varAng = angles.Average(a =>
                {
                    var d = a - meanAng;
                    while (d > Math.PI) d -= (float)(2 * Math.PI);
                    while (d < -Math.PI) d += (float)(2 * Math.PI);
                    return d * d;
                });
            }

            if (angVarAndMin.HasValue && varAng < angVarAndMin.Value)
                return $"orbit: AND supplement varAng={varAng:F4} < {angVarAndMin.Value}";

            if (rLo <= 0f)
                return "orbit: harness fail — R_lo must be > 0 (no soft ceiling-only)";

            return null;
        }

        /// <summary>
        /// Wall-clock sticky dwell: explicit dt; candidate age ≥ T_dwell over ≥3 samples before switch.
        /// AmbushStickySwitchDwellSeconds must be asserted by caller (value), not merely set.
        /// </summary>
        public static string? StickySwitchDwellWallClock(
            IReadOnlyList<(float candidateAgeSeconds, long stickyId, long? candidateId)> samples,
            float dwellSeconds,
            float dt,
            long initialStickyId,
            long switchToId,
            int minAgeSamplesAtOrAboveDwell = 3)
        {
            if (dwellSeconds <= 0f)
                return "dwell: AmbushStickySwitchDwellSeconds must be asserted > 0";
            if (dt <= 0f)
                return "dwell: explicit dt required";
            if (samples == null || samples.Count < 4)
                return $"dwell: need ≥4 samples, got {samples?.Count ?? 0}";

            int firstSwitch = -1;
            for (int i = 0; i < samples.Count; i++)
            {
                if (samples[i].stickyId == switchToId)
                {
                    firstSwitch = i;
                    break;
                }
            }
            if (firstSwitch < 0)
                return $"dwell: never switched to {switchToId}";

            // Before switch: sticky stays on initial while candidate age accumulates (Commit clears age).
            int progressSamples = 0;
            for (int i = 0; i < firstSwitch; i++)
            {
                if (samples[i].stickyId != initialStickyId)
                    return $"dwell: sticky left {initialStickyId} before dwell completed at sample {i}";
                if (samples[i].candidateAgeSeconds > 1e-4f)
                    progressSamples++;
            }
            if (progressSamples < minAgeSamplesAtOrAboveDwell)
                return $"dwell: only {progressSamples} pre-switch samples with candidate age>0 (need ≥{minAgeSamplesAtOrAboveDwell})";

            // Wall-clock: last pre-switch age + explicit dt must reach T_dwell (Commit clears age on switch tick).
            if (firstSwitch == 0)
                return "dwell: switched on first sample without dwell accumulation";
            var prevAge = samples[firstSwitch - 1].candidateAgeSeconds;
            if (prevAge + dt + 1e-4f < dwellSeconds)
                return $"dwell: switch at sample {firstSwitch} before candidate age ≥ {dwellSeconds}s "
                    + $"(prevAge={prevAge:F3}, prevAge+dt={prevAge + dt:F3})";

            // Implied age at switch tick ≥ dwell — count as sustained (≥3 with progress + switch).
            int sustained = progressSamples;
            if (prevAge + dt + 1e-4f >= dwellSeconds)
                sustained++;
            if (sustained < minAgeSamplesAtOrAboveDwell)
                return $"dwell: sustained age samples {sustained} < {minAgeSamplesAtOrAboveDwell}";

            return null;
        }

        /// <summary>
        /// (4) Pack-as-Unit: σ≤σ_max ≥90%; α≤35°; ε_rel≤3–4; alignFrac≥0.75.
        /// </summary>
        public static string? PackAsUnit(
            IReadOnlyList<TickMetrics> hist,
            float sigmaMax = 8f,
            float sigmaFraction = 0.90f,
            float alphaDeg = 35f,
            float epsRel = 4.0f,
            float alignFracMin = 0.75f,
            float playerTranslateMin = 8f,
            int minMobs = 3,
            int minTicks = 12,
            int warmup = 3)
        {
            if (hist == null || hist.Count < minTicks)
                return $"pack: need N≥{minTicks}, got {hist?.Count ?? 0}";
            if (sigmaMax > 8.01f)
                return $"pack: harness fail — σ_max={sigmaMax} must be ≤8 (loose body soft-pass)";

            var pathLen = PlayerPathLength(hist);
            if (pathLen < playerTranslateMin)
                return $"pack: player translate {pathLen:F1}m < {playerTranslateMin}";

            var steady = AfterWarmup(hist, warmup);
            int okSigma = 0;
            var memberIds = steady[0].Members.Where(m => m.HasIntent).Select(m => m.InstanceId).ToList();
            if (memberIds.Count < minMobs)
                return $"pack: need ≥{minMobs} mobs, got {memberIds.Count}";

            foreach (var h in steady)
            {
                var c = h.PackCentroid;
                var living = h.Members.Where(m => m.HasIntent).ToList();
                if (living.Count < minMobs) continue;
                float sumSq = 0f;
                foreach (var m in living)
                {
                    var d = Dist(m.Position, c);
                    sumSq += d * d;
                }
                var sigma = Mathf.Sqrt(sumSq / living.Count);
                if (sigma <= sigmaMax)
                    okSigma++;
            }

            var frac = (float)okSigma / steady.Count;
            if (frac < sigmaFraction)
                return $"pack: σ≤{sigmaMax} only {frac:P0} of ticks (<{sigmaFraction:P0})";

            float cosMin = Mathf.Cos(alphaDeg * Mathf.Deg2Rad);
            int aligned = 0;
            int alignN = 0;
            var relDrifts = new List<float>();

            for (int i = 1; i < steady.Count; i++)
            {
                var c0 = steady[i - 1].PackCentroid;
                var c1 = steady[i].PackCentroid;
                var dc = Delta(c0, c1);
                if (dc.sqrMagnitude < 1e-4f)
                    continue;
                var dcN = dc.normalized;
                foreach (var id in memberIds)
                {
                    var a = steady[i - 1].Members.FirstOrDefault(m => m.InstanceId == id);
                    var b = steady[i].Members.FirstOrDefault(m => m.InstanceId == id);
                    if (!a.HasIntent || !b.HasIntent) continue;
                    var dm = Delta(a.Position, b.Position);
                    relDrifts.Add((dm - dc).magnitude);
                    if (dm.sqrMagnitude < 1e-4f) continue;
                    alignN++;
                    if (Vector3.Dot(dcN, dm.normalized) >= cosMin)
                        aligned++;
                }
            }

            if (alignN < 3)
                return $"pack: velocity alignment under-sampled ({alignN})";
            var alignFrac = (float)aligned / alignN;
            if (alignFrac < alignFracMin)
                return $"pack: velocity alignment κ={alignFrac:P0} < {alignFracMin:P0} (α>{alphaDeg}°)";

            if (relDrifts.Count > 0)
            {
                relDrifts.Sort();
                var median = relDrifts[relDrifts.Count / 2];
                if (median > epsRel)
                    return $"pack: median ||ΔMi−ΔC||={median:F2} > ε_rel={epsRel}";
            }

            return null;
        }

        /// <summary>
        /// (5a) FormUp without threat: Dist(M,S) drop ≥Δ AND Dist(D,S)≤e.
        /// S must be fixed lattice slot (caller), NOT PackCentroid.
        /// </summary>
        public static string? FormUpNoThreat(
            IReadOnlyList<TickMetrics> hist,
            long memberId,
            Func<TickMetrics, Vector3> slotAt,
            float closeEnough = 5f,
            float minDrop = 8f,
            int minTicks = 8)
        {
            if (hist == null || hist.Count < minTicks)
                return $"formup: need N≥{minTicks}, got {hist?.Count ?? 0}";
            if (closeEnough > 6.01f)
                return $"formup: harness fail — closeEnough={closeEnough} must be ≤6 (was soft 12)";

            var first = hist[0].Members.First(m => m.InstanceId == memberId);
            var last = hist[hist.Count - 1].Members.First(m => m.InstanceId == memberId);
            var s0 = slotAt(hist[0]);
            var s1 = slotAt(hist[hist.Count - 1]);
            // Fixed slot: S must not walk with Ms (PackCentroid soft-pass).
            if (Dist(s0, s1) > 1.5f)
                return $"formup: harness fail — slot walked {Dist(s0, s1):F1}m (PackCentroid-as-slot?)";

            var d0 = Dist(first.Position, s0);
            var d1 = Dist(last.Position, s1);
            if (d0 - d1 < minDrop && d1 > closeEnough)
                return $"formup: Dist(M,S) drop {d0:F1}→{d1:F1} < Δ={minDrop} and not close";

            // H3: Dist(D,S)≤e OR arrived (M near S and D near M) — live magnet slot may nudge with facing.
            var dDes = Dist(last.DesiredPosition, s1);
            var dDM = Dist(last.DesiredPosition, last.Position);
            // Arrived: M near S. Live magnet Desired may sit on nudged slot ≤e from M.
            if (d1 <= closeEnough)
            {
                if (dDM > closeEnough)
                    return $"formup: arrived M but Dist(D,M)={dDM:F1} > e={closeEnough}";
            }
            else if (dDes > closeEnough)
                return $"formup: Dist(D,S)={dDes:F1} > e={closeEnough} (M not yet at S)";

            if (d1 > closeEnough && d0 - d1 < minDrop)
                return $"formup: Dist(M,S) {d0:F1}→{d1:F1} did not satisfy drop≥Δ or close";

            return null;
        }

        public static string? ChargeTowardThreat(
            IReadOnlyList<TickMetrics> hist,
            long memberId,
            Func<TickMetrics, Vector3> threatAt,
            Func<TickMetrics, Vector3> formUpSlotAt,
            float slotMargin = 4f,
            float minClose = 1.0f,
            int minTicks = 8)
        {
            if (hist == null || hist.Count < minTicks)
                return $"charge: need N≥{minTicks}, got {hist?.Count ?? 0}";
            if (formUpSlotAt == null)
                return "charge: harness fail — FormUp slot required (not null / not PackCentroid soft)";

            int toward = 0;
            int samples = 0;
            int betterThanSlot = 0;
            foreach (var h in hist)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == memberId && x.HasIntent);
                if (!m.HasIntent) continue;
                var t = threatAt(h);
                var slot = formUpSlotAt(h);
                var toThreat = Xz(t) - Xz(m.Position);
                var toDesired = Xz(m.DesiredPosition) - Xz(m.Position);
                samples++;
                if (toDesired.sqrMagnitude < 1e-6f) continue;
                if (Vector3.Dot(toThreat, toDesired) > 0f
                    && Dist(m.DesiredPosition, t) < Dist(m.Position, t) + 0.01f)
                    toward++;

                // H8: D closer to T than to S by margin.
                var dT = Dist(m.DesiredPosition, t);
                var dS = Dist(m.DesiredPosition, slot);
                if (dT + slotMargin < dS)
                    betterThanSlot++;
                else if (dS + 2f < dT && dS < 10f)
                    return $"charge: Desired snapped to FormUp slot (distSlot={dS:F1}, distT={dT:F1})";
            }

            if (samples < minTicks)
                return $"charge: sparse samples {samples}";
            if ((float)toward / samples < 0.70f)
                return $"charge: Desired toward threat only {toward}/{samples} ticks";
            if ((float)betterThanSlot / samples < 0.70f)
                return $"charge: D closer to T than S by margin only {betterThanSlot}/{samples}";

            var first = hist[0].Members.First(m => m.InstanceId == memberId);
            var last = hist[hist.Count - 1].Members.First(m => m.InstanceId == memberId);
            var d0 = Dist(first.Position, threatAt(hist[0]));
            var d1 = Dist(last.Position, threatAt(hist[hist.Count - 1]));
            if (d1 + minClose > d0)
                return $"charge: Position not closing on threat {d0:F1}→{d1:F1} (need drop ≥{minClose})";

            return null;
        }

        /// <summary>
        /// (6) Zero/soft magnet H2: magnet ON — Dist(M,P), Dist(D,P) ∈ [R_lo,R_hi] not →0;
        /// magnet OFF — K≥6 ticks Dist non-decreasing + Desired not on P.
        /// </summary>
        public static string? ZeroMagnetUnsnapped(
            IReadOnlyList<TickMetrics> magnetOnHist,
            IReadOnlyList<TickMetrics> magnetOffHist,
            long memberId,
            Func<TickMetrics, Vector3> playerAt,
            float rLo,
            float rHi,
            int offK = 6)
        {
            if (magnetOnHist == null || magnetOnHist.Count < 4)
                return $"zeromag: magnet-ON need N≥4, got {magnetOnHist?.Count ?? 0}";
            if (magnetOffHist == null || magnetOffHist.Count < offK)
                return $"zeromag: magnet-OFF need N≥{offK}, got {magnetOffHist?.Count ?? 0}";
            if (rLo < 1f)
                return "zeromag: harness fail — R_lo must be ≥1 (not FormUpMagnetDistance=0 fake chase)";

            foreach (var h in magnetOnHist)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == memberId && x.HasIntent);
                if (!m.HasIntent)
                    return $"zeromag:ON missing intent tick {h.TickIndex}";
                var p = playerAt(h);
                var dm = Dist(m.Position, p);
                var dd = Dist(m.DesiredPosition, p);
                if (dm < rLo || dm > rHi)
                    return $"zeromag:ON Dist(M,P)={dm:F1} not in [{rLo},{rHi}] tick {h.TickIndex}";
                if (dd < rLo || dd > rHi)
                    return $"zeromag:ON Dist(D,P)={dd:F1} not in [{rLo},{rHi}] tick {h.TickIndex} (collapsed or unbound)";
            }

            var off = magnetOffHist.Take(offK).ToList();
            float? prev = null;
            foreach (var h in off)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == memberId && x.HasIntent);
                if (!m.HasIntent)
                    return $"zeromag:OFF missing intent tick {h.TickIndex}";
                var p = playerAt(h);
                var dm = Dist(m.Position, p);
                var dd = Dist(m.DesiredPosition, p);
                if (dd < rLo)
                    return $"zeromag:OFF Desired on P Dist(D,P)={dd:F1} < {rLo}";
                if (prev.HasValue && dm + 0.05f < prev.Value)
                    return $"zeromag:OFF Dist(M,P) decreased {prev.Value:F1}→{dm:F1} (re-attracted with magnet off)";
                prev = dm;
            }

            return null;
        }

        /// <summary>
        /// (7) Theater geometry H7: Pin band tight + mean|Δφ|≤0.25; Flank band + half-plane ≥80%;
        /// Harass outer; multi-tick Assign supporting Fact at caller.
        /// </summary>
        public static string? TheaterPinFlankHarass(
            IReadOnlyList<TickMetrics> pinHist,
            IReadOnlyList<TickMetrics> flankHist,
            IReadOnlyList<TickMetrics>? harassHist,
            Func<TickMetrics, Vector3> focusAt,
            float pinRLo = 4f,
            float pinRHi = 12f,
            float flankRLo = 8f,
            float flankRHi = 22f,
            float harassRLo = 14f,
            float bandFraction = 0.80f,
            float pinPhiMax = 0.25f,
            float playerPathMin = 10f,
            int minTicks = 16,
            int warmup = 4)
        {
            if (pinHist == null || pinHist.Count < minTicks)
                return $"theater: Pin N={pinHist?.Count ?? 0} < {minTicks}";
            if (flankHist == null || flankHist.Count < minTicks)
                return $"theater: Flank N={flankHist?.Count ?? 0} < {minTicks}";

            var pathLen = PlayerPathLength(pinHist);
            if (pathLen < playerPathMin)
                return $"theater: player path {pathLen:F1}m < {playerPathMin}";

            string? BandCheck(IReadOnlyList<TickMetrics> hist, float lo, float hi, string label)
            {
                var steady = AfterWarmup(hist, warmup);
                int ok = 0, n = 0;
                foreach (var h in steady)
                {
                    var f = focusAt(h);
                    foreach (var m in h.Members.Where(x => x.HasIntent))
                    {
                        n++;
                        var d = Dist(m.Position, f);
                        if (d >= lo && d <= hi) ok++;
                    }
                }
                if (n == 0) return $"theater:{label} no samples";
                if ((float)ok / n < bandFraction)
                    return $"theater:{label} Dist band [{lo},{hi}] only {(float)ok / n:P0}";
                return null;
            }

            var e = BandCheck(pinHist, pinRLo, pinRHi, "Pin");
            if (e != null) return e;
            e = BandCheck(flankHist, flankRLo, flankRHi, "Flank");
            if (e != null) return e;
            if (harassHist != null)
            {
                e = BandCheck(harassHist, harassRLo, flankRHi + 10f, "Harass");
                if (e != null) return e;
            }

            var pinSteady = AfterWarmup(pinHist, warmup);
            float sumAbsDPhi = 0f;
            float? prev = null;
            int phiN = 0;
            for (int i = 0; i < pinSteady.Count; i++)
            {
                var h = pinSteady[i];
                var f = focusAt(h);
                var mean = Vector3.zero;
                int c = 0;
                foreach (var m in h.Members.Where(x => x.HasIntent))
                {
                    mean += Xz(m.Position) - Xz(f);
                    c++;
                }
                if (c == 0) continue;
                mean /= c;
                var phi = Mathf.Atan2(mean.z, mean.x);
                if (prev.HasValue)
                {
                    var d = phi - prev.Value;
                    while (d > Math.PI) d -= (float)(2 * Math.PI);
                    while (d < -Math.PI) d += (float)(2 * Math.PI);
                    sumAbsDPhi += Math.Abs(d);
                    phiN++;
                }
                prev = phi;
            }
            var meanDPhi = phiN > 0 ? sumAbsDPhi / phiN : 0f;
            if (meanDPhi > pinPhiMax)
                return $"theater:Pin |Δφ| mean={meanDPhi:F3} > {pinPhiMax} (not stable front)";

            int sameHalf = 0, halfN = 0;
            var flankSteady = AfterWarmup(flankHist, warmup);
            float? flankSign = null;
            for (int i = 1; i < Math.Min(pinSteady.Count, flankSteady.Count); i++)
            {
                var f0 = focusAt(pinSteady[i - 1]);
                var f1 = focusAt(pinSteady[i]);
                var forward = Delta(f0, f1);
                if (forward.sqrMagnitude < 1e-4f) continue;
                var right = Vector3.Cross(Vector3.up, forward.normalized);

                Vector3 MeanOff(TickMetrics h)
                {
                    var f = focusAt(h);
                    var sum = Vector3.zero;
                    int n = 0;
                    foreach (var m in h.Members.Where(x => x.HasIntent))
                    {
                        sum += Xz(m.Position) - Xz(f);
                        n++;
                    }
                    return n == 0 ? Vector3.zero : sum / n;
                }

                var pinLat = Vector3.Dot(MeanOff(pinSteady[i]), right);
                var flankLat = Vector3.Dot(MeanOff(flankSteady[i]), right);
                if (Math.Abs(flankLat - pinLat) < 3f)
                    continue;
                halfN++;
                var sign = Math.Sign(flankLat);
                if (flankSign == null) flankSign = sign;
                if (sign == flankSign) sameHalf++;
            }

            if (halfN < 4)
                return $"theater:Flank half-plane under-sampled ({halfN}) — packs not laterally separated";
            if ((float)sameHalf / halfN < bandFraction)
                return $"theater:Flank half-plane unstable {sameHalf}/{halfN}";

            return null;
        }

        public static void Require(string? error, string claim)
        {
            if (error != null)
                Xunit.Assert.Fail($"[{claim}] {error}");
        }
    }
}
