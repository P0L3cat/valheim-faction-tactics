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
    /// </summary>
    internal static class MotionPredicates
    {
        public const float DefaultWarmupFraction = 0.25f;

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
        /// (1) PreferRun/flee: threat T inject Dist(M,T)&lt;R_threat; radial outward escape
        /// until Dist grows by Δ_escape. mean (M−T)·unit(ΔM) &gt; cosθ; mean||Δ||≥v_min*dt.
        /// </summary>
        public static string? FleeRadialOutward(
            IReadOnlyList<TickMetrics> hist,
            Func<TickMetrics, Vector3> threatAt,
            long memberId,
            float rThreat,
            float cosThetaMin = 0.707f,
            float vMin = 2.0f,
            float dt = 0.25f,
            float deltaEscape = 1.0f,
            int minTicks = 8)
        {
            if (hist == null || hist.Count < minTicks)
                return $"flee: need N≥{minTicks}, got {hist?.Count ?? 0}";

            // Build escape window: from first threatened tick until Dist gained ≥ deltaEscape
            // or we leave R_threat. Reject if never threatened.
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
                    escape.Add(h);
                    continue;
                }
                escape.Add(h);
                if (d >= dStart.Value + deltaEscape || d >= rThreat)
                    break;
            }

            if (escape.Count < 3 || dStart == null)
                return $"flee: no Dist(M,T)<{rThreat} escape window (start never inside threat)";

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
            var minStep = vMin * dt * 0.5f; // half — escape can include partial slot approaches
            if (meanStep < minStep)
                return $"flee: mean||Delta||={meanStep:F3} < {minStep:F3}";

            var first = escape[0].Members.First(x => x.InstanceId == memberId);
            var last = escape[escape.Count - 1].Members.First(x => x.InstanceId == memberId);
            var d0 = Dist(first.Position, threatAt(escape[0]));
            var d1 = Dist(last.Position, threatAt(escape[escape.Count - 1]));
            if (d1 + 1e-3f < d0 + deltaEscape)
                return $"flee: Dist(end,T)={d1:F2} < Dist(start,T)+Δ={d0 + deltaEscape:F2}";

            if (nDes > 0 && sumDesCos / nDes < 0f)
                return $"flee: Desired mean radial cos={sumDesCos / nDes:F3} points inward";

            return null;
        }

        /// <summary>
        /// (2) Straggler merge: Dist(Ms,C) from ≥R_out to ≤R_in; pack coherence last K ticks.
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

            var first = hist[0].Members.FirstOrDefault(m => m.InstanceId == stragglerId && m.HasIntent);
            var last = hist[hist.Count - 1].Members.FirstOrDefault(m => m.InstanceId == stragglerId && m.HasIntent);
            if (!first.HasIntent || !last.HasIntent)
                return "merge: straggler missing intent at start/end";

            var d0 = Dist(first.Position, hist[0].PackCentroid);
            var d1 = Dist(last.Position, hist[hist.Count - 1].PackCentroid);
            if (d0 < rOut)
                return $"merge: start Dist(Ms,C)={d0:F1} < R_out={rOut} (not a real straggler)";
            if (d1 > rIn)
                return $"merge: end Dist(Ms,C)={d1:F1} > R_in={rIn} (Ms did not close)";

            // Position must actually move toward centroid (reject frozen Position + soft Desired.x).
            var moved = Dist(first.Position, last.Position);
            if (moved < (d0 - d1) * 0.35f)
                return $"merge: Position nearly frozen moved={moved:F1} while gap {d0:F1}→{d1:F1}";

            var tail = hist.Skip(Math.Max(0, hist.Count - lastK)).ToList();
            foreach (var h in tail)
            {
                foreach (var m in h.Members.Where(x => x.HasIntent && x.InstanceId != stragglerId))
                {
                    var d = Dist(m.Position, h.PackCentroid);
                    if (d > rPack)
                        return $"merge: non-straggler {m.InstanceId} Dist(C)={d:F1} > R_pack={rPack} on late tick {h.TickIndex}";
                }
            }

            // Optional supporting: Desired→centroid shrinks.
            var des0 = Dist(first.DesiredPosition, hist[0].PackCentroid);
            var des1 = Dist(last.DesiredPosition, hist[hist.Count - 1].PackCentroid);
            if (des1 > des0 + 1f && des1 > rIn + 5f)
                return $"merge: Desired drifted away from centroid {des0:F1}→{des1:F1}";

            return null;
        }

        /// <summary>
        /// (3) Ambush sticky orbit AND: flaps≤F_max, sticky held, Dist(M,P)∈[R_lo,R_hi] ≥80%,
        /// angular progress, player path length.
        /// </summary>
        public static string? AmbushStickyOrbit(
            IReadOnlyList<TickMetrics> hist,
            float rLo,
            float rHi,
            float bandFraction = 0.80f,
            int flapsMax = 0,
            float phiMin = 1.57f,
            float angVarMin = 0.05f,
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

            var steady = AfterWarmup(hist, warmup);
            int inBand = 0;
            int samples = 0;
            var angles = new List<float>();
            float sumAbsDPhi = 0f;
            float? prevMeanPhi = null;

            foreach (var h in steady)
            {
                var p = h.StickyPosition;
                foreach (var m in h.Members.Where(x => x.HasIntent))
                {
                    var d = Dist(m.Position, p);
                    samples++;
                    if (d >= rLo && d <= rHi)
                        inBand++;
                    var off = Xz(m.Position) - Xz(p);
                    if (off.sqrMagnitude > 0.25f)
                        angles.Add(Mathf.Atan2(off.z, off.x));
                }

                // Pack mean angle progress around sticky.
                var offs = h.Members.Where(x => x.HasIntent)
                    .Select(m => Xz(m.Position) - Xz(p))
                    .Where(o => o.sqrMagnitude > 0.25f)
                    .ToList();
                if (offs.Count >= 2)
                {
                    var mean = offs.Aggregate(Vector3.zero, (a, b) => a + b) / offs.Count;
                    var phi = Mathf.Atan2(mean.z, mean.x);
                    if (prevMeanPhi.HasValue)
                    {
                        var dphi = phi - prevMeanPhi.Value;
                        while (dphi > Math.PI) dphi -= (float)(2 * Math.PI);
                        while (dphi < -Math.PI) dphi += (float)(2 * Math.PI);
                        sumAbsDPhi += Math.Abs(dphi);
                    }
                    prevMeanPhi = phi;
                }
            }

            if (samples == 0)
                return "orbit: no member samples";
            var frac = (float)inBand / samples;
            if (frac < bandFraction)
                return $"orbit: Dist(M,P) in [{rLo},{rHi}] only {frac:P0} (<{bandFraction:P0})";

            // Angular: either cumulative mean-phi travel OR spread variance across members.
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

            if (sumAbsDPhi < phiMin && varAng < angVarMin)
                return $"orbit: angular dead — |Δφ|sum={sumAbsDPhi:F3} < {phiMin} and var={varAng:F4} < {angVarMin}";

            // Soft ceiling alone is rejected by requiring lower band rLo > 0.
            if (rLo <= 0f)
                return "orbit: harness fail — R_lo must be > 0 (no soft ceiling-only)";

            return null;
        }

        /// <summary>
        /// (4) Pack-as-Unit: σ=rms||Mi−C||≤σ_max ≥90% ticks; velocity alignment; relative drift.
        /// </summary>
        public static string? PackAsUnit(
            IReadOnlyList<TickMetrics> hist,
            float sigmaMax,
            float sigmaFraction = 0.90f,
            float alphaDeg = 35f,
            float epsRel = 4.0f,
            float playerTranslateMin = 8f,
            int minMobs = 3,
            int minTicks = 12,
            int warmup = 3)
        {
            if (hist == null || hist.Count < minTicks)
                return $"pack: need N≥{minTicks}, got {hist?.Count ?? 0}";

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

            // Common velocity alignment: mean member Delta vs centroid Delta.
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

            if (alignN > 0)
            {
                var alignFrac = (float)aligned / alignN;
                if (alignFrac < 0.55f)
                    return $"pack: velocity alignment κ={alignFrac:P0} (α>{alphaDeg}°) too low";
            }

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
        /// (5a) FormUp without threat: Dist(M,S) drops OR M tracks D toward slot.
        /// </summary>
        public static string? FormUpNoThreat(
            IReadOnlyList<TickMetrics> hist,
            long memberId,
            Func<TickMetrics, Vector3> slotAt,
            float closeEnough = 4f,
            int minTicks = 8)
        {
            if (hist == null || hist.Count < minTicks)
                return $"formup: need N≥{minTicks}, got {hist?.Count ?? 0}";

            var first = hist[0].Members.First(m => m.InstanceId == memberId);
            var last = hist[hist.Count - 1].Members.First(m => m.InstanceId == memberId);
            var s0 = slotAt(hist[0]);
            var s1 = slotAt(hist[hist.Count - 1]);
            var d0 = Dist(first.Position, s0);
            var d1 = Dist(last.Position, s1);
            var tracksDesired = Dist(last.Position, last.DesiredPosition) < closeEnough
                                || Dist(last.DesiredPosition, s1) < closeEnough + 2f;

            if (!(d1 + 0.5f < d0 || (d1 <= closeEnough && tracksDesired)))
                return $"formup: Dist(M,S) {d0:F1}→{d1:F1} did not drop and M not tracking D/slot";

            // Desired must point at slot, not threat-chase.
            if (Dist(last.DesiredPosition, s1) > closeEnough + 8f)
                return $"formup: late Desired far from slot Dist={Dist(last.DesiredPosition, s1):F1}";

            return null;
        }

        /// <summary>
        /// (5b) Charge WITH threat inject: D not FormUp slot; (T−M)·(D−M)>0; Dist(D,T)&lt;Dist(M,T); closing.
        /// </summary>
        public static string? ChargeTowardThreat(
            IReadOnlyList<TickMetrics> hist,
            long memberId,
            Func<TickMetrics, Vector3> threatAt,
            Func<TickMetrics, Vector3>? formUpSlotAt,
            float minClose = 1.0f,
            int minTicks = 8)
        {
            if (hist == null || hist.Count < minTicks)
                return $"charge: need N≥{minTicks}, got {hist?.Count ?? 0}";

            int toward = 0;
            int samples = 0;
            foreach (var h in hist)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == memberId && x.HasIntent);
                if (!m.HasIntent) continue;
                var t = threatAt(h);
                var toThreat = Xz(t) - Xz(m.Position);
                var toDesired = Xz(m.DesiredPosition) - Xz(m.Position);
                samples++;
                if (toDesired.sqrMagnitude < 1e-6f) continue;
                if (Vector3.Dot(toThreat, toDesired) > 0f
                    && Dist(m.DesiredPosition, t) < Dist(m.Position, t) + 0.01f)
                    toward++;

                if (formUpSlotAt != null)
                {
                    var slot = formUpSlotAt(h);
                    // Desired should be nearer threat than slot when charging.
                    if (Dist(m.DesiredPosition, t) > Dist(m.DesiredPosition, slot) + 2f
                        && Dist(m.DesiredPosition, slot) < 8f)
                        return $"charge: Desired snapped to FormUp slot (distSlot={Dist(m.DesiredPosition, slot):F1}, distT={Dist(m.DesiredPosition, t):F1})";
                }
            }

            if (samples < minTicks)
                return $"charge: sparse samples {samples}";
            if ((float)toward / samples < 0.70f)
                return $"charge: Desired toward threat only {toward}/{samples} ticks";

            var first = hist[0].Members.First(m => m.InstanceId == memberId);
            var last = hist[hist.Count - 1].Members.First(m => m.InstanceId == memberId);
            var d0 = Dist(first.Position, threatAt(hist[0]));
            var d1 = Dist(last.Position, threatAt(hist[hist.Count - 1]));
            if (d1 + minClose > d0)
                return $"charge: Position not closing on threat {d0:F1}→{d1:F1} (need drop ≥{minClose})";

            return null;
        }

        /// <summary>
        /// (6) Zero/soft magnet: Dist(M,P) and Dist(D,P) stay ≥ floors; after magnet off stay unsnapped.
        /// </summary>
        public static string? ZeroMagnetUnsnapped(
            IReadOnlyList<TickMetrics> hist,
            long memberId,
            Func<TickMetrics, Vector3> playerAt,
            float rFloor,
            float rFloorD,
            int minTicks = 10,
            int lastK = 6)
        {
            if (hist == null || hist.Count < minTicks)
                return $"zeromag: need N≥{minTicks}, got {hist?.Count ?? 0}";

            foreach (var h in hist)
            {
                var m = h.Members.FirstOrDefault(x => x.InstanceId == memberId && x.HasIntent);
                if (!m.HasIntent)
                    return $"zeromag: missing intent tick {h.TickIndex}";
                var p = playerAt(h);
                var dm = Dist(m.Position, p);
                var dd = Dist(m.DesiredPosition, p);
                if (dm < rFloor)
                    return $"zeromag: Dist(M,P)={dm:F1} < R_floor={rFloor} tick {h.TickIndex}";
                if (dd < rFloorD)
                    return $"zeromag: Dist(D,P)={dd:F1} < R_floor_D={rFloorD} tick {h.TickIndex} (Desired collapsed to player)";
            }

            var tail = hist.Skip(Math.Max(0, hist.Count - lastK)).ToList();
            foreach (var h in tail)
            {
                var m = h.Members.First(x => x.InstanceId == memberId);
                var p = playerAt(h);
                if (Dist(m.Position, p) < rFloor || Dist(m.DesiredPosition, p) < rFloorD)
                    return "zeromag: late-K re-snapped to player";
            }

            return null;
        }

        /// <summary>
        /// (7) Theater geometry: Pin band + low |Δφ|; Flank half-plane; Harass outer; multi-tick Assign.
        /// Compared across role-tagged trajectories (caller supplies per-role member series).
        /// </summary>
        public static string? TheaterPinFlankHarass(
            IReadOnlyList<TickMetrics> pinHist,
            IReadOnlyList<TickMetrics> flankHist,
            IReadOnlyList<TickMetrics>? harassHist,
            Func<TickMetrics, Vector3> focusAt,
            float pinRLo,
            float pinRHi,
            float flankRLo,
            float flankRHi,
            float harassRLo,
            float bandFraction = 0.80f,
            float pinPhiMax = 0.70f,
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
                e = BandCheck(harassHist, harassRLo, flankRHi + 8f, "Harass");
                if (e != null) return e;
            }

            // Pin: low angular wander of pack mean relative to focus forward (player motion dir).
            var pinSteady = AfterWarmup(pinHist, warmup);
            float sumAbsDPhi = 0f;
            float? prev = null;
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
                }
                prev = phi;
            }
            var meanDPhi = pinSteady.Count > 1 ? sumAbsDPhi / (pinSteady.Count - 1) : 0f;
            if (meanDPhi > pinPhiMax)
                return $"theater:Pin |Δφ| mean={meanDPhi:F3} > {pinPhiMax} (not stable front)";

            // Flank stable half-plane vs Pin mean lateral sign of ĥ (right of player forward).
            // Use player motion as ĥ forward; right = cross(up, forward).
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
                // Flank must not share Pin's near-zero lateral plane.
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
