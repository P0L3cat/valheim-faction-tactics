using System;
using FactionTactics.Squad;

namespace FactionTactics.Doctrine
{
    /// <summary>
    /// Tunables for one banded doctrine (Roman or VikingShieldWall).
    /// Swing is clamped inside the standoff so press has somewhere to go.
    /// </summary>
    public readonly struct CadenceProfile
    {
        public float StandoffDistance { get; }
        public float SwingRange { get; }
        public float HoldMin { get; }
        public float HoldMax { get; }
        public float RetreatPauseSeconds { get; }
        public float StandoffExitMargin { get; }
        public float RetreatOpenMargin { get; }

        public CadenceProfile(
            float standoffDistance,
            float swingRange,
            float holdMin,
            float holdMax,
            float retreatPauseSeconds,
            float standoffExitMargin,
            float retreatOpenMargin)
        {
            StandoffDistance = standoffDistance;
            SwingRange = swingRange;
            HoldMin = holdMin;
            HoldMax = holdMax;
            RetreatPauseSeconds = retreatPauseSeconds;
            StandoffExitMargin = standoffExitMargin;
            RetreatOpenMargin = retreatOpenMargin;
        }

        public static CadenceProfile Resolve(
            float standoff,
            float swing,
            float holdMin,
            float holdMax,
            float pause,
            float exitMargin = 2.5f,
            float retreatMargin = 1.25f)
        {
            if (float.IsNaN(standoff) || float.IsInfinity(standoff) || standoff < 6f)
                standoff = 20f;
            if (float.IsNaN(swing) || float.IsInfinity(swing) || swing < 1f)
                swing = 3.5f;
            if (swing > standoff - 2f)
                swing = Math.Max(1f, standoff - 2f);
            if (float.IsNaN(holdMin) || holdMin < 0.25f)
                holdMin = 1f;
            if (float.IsNaN(holdMax) || holdMax < holdMin)
                holdMax = holdMin;
            if (float.IsNaN(pause) || pause < 0f)
                pause = 1f;
            if (float.IsNaN(exitMargin) || exitMargin < 0.5f)
                exitMargin = 2.5f;
            if (float.IsNaN(retreatMargin) || retreatMargin < 0.25f)
                retreatMargin = 1.25f;
            return new CadenceProfile(standoff, swing, holdMin, holdMax, pause, exitMargin, retreatMargin);
        }
    }

    /// <summary>
    /// Pure standoff → hold → press → contact → retreat-pause machine.
    /// Clock is squad <see cref="SquadSnapshot.SquadAgeSeconds"/> (not Unity time) so tests
    /// and discovery rebuilds share one timeline. Hysteresis must not freeze a press:
    /// callers treat <see cref="SquadSnapshot.CadenceTimerElapsed"/> as a dwell bypass.
    /// </summary>
    public static class BandedCadence
    {
        private static readonly object RollGate = new object();
        private static readonly Random RollRng = new Random();

        /// <summary>Distance the cadence watches: front line when known, else nearest member.</summary>
        public static float BandDistance(SquadSnapshot snapshot)
        {
            if (snapshot == null)
                return float.MaxValue;
            var front = snapshot.FrontlineThreatDistance;
            if (!float.IsNaN(front) && !float.IsInfinity(front) && front < 1.0e8f)
                return front;
            return snapshot.NearestThreatDistance;
        }

        public static DoctrineOrderKind OrderFor(RomanPhase phase)
        {
            switch (phase)
            {
                case RomanPhase.ApproachStandoff:
                case RomanPhase.PressContact:
                    return DoctrineOrderKind.Advance;
                case RomanPhase.StandoffHold:
                case RomanPhase.ContactHold:
                case RomanPhase.RetreatPause:
                    return DoctrineOrderKind.Hold;
                default:
                    return DoctrineOrderKind.Hold;
            }
        }

        public static void Reset(SquadSnapshot snapshot)
        {
            if (snapshot == null)
                return;
            snapshot.RomanPhase = RomanPhase.Idle;
            snapshot.HoldPhaseDeadline = 0f;
            snapshot.StandoffHoldDuration = 0f;
            snapshot.CadenceTimerElapsed = false;
            snapshot.PreviousThreatDistance = float.MaxValue;
        }

        /// <summary>Inclusive integer seconds in [min, max].</summary>
        public static float RollHoldSeconds(float min, float max)
        {
            var lo = (int)Math.Floor(min);
            var hi = (int)Math.Ceiling(max);
            if (lo < 1)
                lo = 1;
            if (hi < lo)
                hi = lo;
            lock (RollGate)
                return RollRng.Next(lo, hi + 1);
        }

        public static void Step(SquadSnapshot snapshot, CadenceProfile profile)
        {
            if (snapshot == null)
                return;

            var phase = snapshot.RomanPhase;
            var deadline = snapshot.HoldPhaseDeadline;
            var duration = snapshot.StandoffHoldDuration;
            var age = snapshot.SquadAgeSeconds;
            var d = BandDistance(snapshot);
            var prevD = snapshot.PreviousThreatDistance;
            var timerElapsed = false;

            var standoff = profile.StandoffDistance;
            var swing = profile.SwingRange;
            var exit = standoff + profile.StandoffExitMargin;
            var retreatLine = swing + profile.RetreatOpenMargin;

            switch (phase)
            {
                case RomanPhase.RetreatPause:
                    if (d <= swing)
                    {
                        phase = RomanPhase.ContactHold;
                    }
                    else if (deadline > 0f && age >= deadline)
                    {
                        timerElapsed = true;
                        phase = RomanPhase.PressContact;
                    }
                    else if (deadline <= 0f)
                    {
                        deadline = age + profile.RetreatPauseSeconds;
                    }
                    break;

                case RomanPhase.ContactHold:
                    if (d > retreatLine)
                    {
                        deadline = age + profile.RetreatPauseSeconds;
                        phase = RomanPhase.RetreatPause;
                    }
                    break;

                case RomanPhase.PressContact:
                    if (d <= swing)
                    {
                        phase = RomanPhase.ContactHold;
                    }
                    else if (prevD <= retreatLine && d > retreatLine && d > prevD + 0.35f)
                    {
                        // Were in the swing margin and the gap opened — 1s pause, then press.
                        deadline = age + profile.RetreatPauseSeconds;
                        phase = RomanPhase.RetreatPause;
                    }
                    else if (d > exit)
                    {
                        phase = RomanPhase.ApproachStandoff;
                    }
                    break;

                case RomanPhase.StandoffHold:
                    if (d <= swing)
                    {
                        phase = RomanPhase.ContactHold;
                    }
                    else if (d > exit)
                    {
                        phase = RomanPhase.ApproachStandoff;
                    }
                    else if (deadline > 0f && age >= deadline)
                    {
                        timerElapsed = true;
                        phase = RomanPhase.PressContact;
                    }
                    else if (deadline <= 0f)
                    {
                        deadline = age + (duration > 0.05f ? duration : profile.HoldMin);
                    }
                    break;

                case RomanPhase.Idle:
                case RomanPhase.ApproachStandoff:
                default:
                    if (d <= swing)
                    {
                        // Already in melee — contact hold, don't pretend we are still at 20m.
                        phase = RomanPhase.ContactHold;
                    }
                    else if (d <= standoff)
                    {
                        phase = EnterStandoff(snapshot, profile, age, ref deadline, ref duration);
                    }
                    else
                    {
                        phase = RomanPhase.ApproachStandoff;
                    }
                    break;
            }

            snapshot.RomanPhase = phase;
            snapshot.HoldPhaseDeadline = deadline;
            snapshot.StandoffHoldDuration = duration;
            snapshot.CadenceTimerElapsed = timerElapsed;
            snapshot.ActiveStandoffDistance = standoff;
            snapshot.ActiveSwingRange = swing;
        }

        public static void ReadInto(SquadSnapshot snapshot, SquadRuntimeState state)
        {
            if (snapshot == null || state == null)
                return;
            snapshot.RomanPhase = state.RomanPhase;
            snapshot.HoldPhaseDeadline = state.HoldPhaseDeadline;
            snapshot.StandoffHoldDuration = state.StandoffHoldDuration;
            snapshot.PreviousThreatDistance = state.PreviousThreatDistance;
            snapshot.SquadAgeSeconds = state.AgeSeconds;
            snapshot.CadenceTimerElapsed = false;
            snapshot.ActiveStandoffDistance = state.ActiveStandoffDistance;
            snapshot.ActiveSwingRange = state.ActiveSwingRange;
        }

        public static void WriteBack(SquadRuntimeState state, SquadSnapshot snapshot)
        {
            if (snapshot == null || state == null)
                return;
            state.RomanPhase = snapshot.RomanPhase;
            state.HoldPhaseDeadline = snapshot.HoldPhaseDeadline;
            state.StandoffHoldDuration = snapshot.StandoffHoldDuration;
            state.PreviousThreatDistance = BandDistance(snapshot);
            state.LastThreatDistance = snapshot.NearestThreatDistance;
            if (snapshot.ActiveStandoffDistance > 0.5f)
                state.ActiveStandoffDistance = snapshot.ActiveStandoffDistance;
            if (snapshot.ActiveSwingRange > 0.5f)
                state.ActiveSwingRange = snapshot.ActiveSwingRange;
        }

        private static RomanPhase EnterStandoff(
            SquadSnapshot snapshot,
            CadenceProfile profile,
            float age,
            ref float deadline,
            ref float duration)
        {
            float seconds;
            if (snapshot.ForcedHoldSeconds.HasValue && !float.IsNaN(snapshot.ForcedHoldSeconds.Value))
            {
                seconds = snapshot.ForcedHoldSeconds.Value;
                if (seconds < profile.HoldMin)
                    seconds = profile.HoldMin;
                if (seconds > profile.HoldMax)
                    seconds = profile.HoldMax;
            }
            else
            {
                seconds = RollHoldSeconds(profile.HoldMin, profile.HoldMax);
            }

            if (seconds < 0.05f)
                seconds = profile.HoldMin;
            duration = seconds;
            deadline = age + seconds;
            return RomanPhase.StandoffHold;
        }
    }
}
