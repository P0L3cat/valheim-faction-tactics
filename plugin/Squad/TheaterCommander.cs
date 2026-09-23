using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using UnityEngine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Cross-squad job for one contact. None = not in a multi-pack battle (or Death-Rush).
    /// </summary>
    public enum TheaterRole
    {
        None = 0,
        Pin = 1,
        Flank = 2,
        Harass = 3,
    }

    /// <summary>One commanding squad passed into <see cref="TheaterCommander.Assign"/>.</summary>
    public sealed class TheaterSquadView
    {
        public SquadRuntimeState State { get; set; } = null!;
        public string DoctrineId { get; set; } = "";
        public string StableId { get; set; } = "";
        public Vector3 Centroid { get; set; }
        public long FocusPlayerId { get; set; }
        public Vector3 FocusPosition { get; set; }
        public bool HasFocus { get; set; }
    }

    /// <summary>
    /// 1.0.11 Theater Commander. When two or more packs share a player inside the
    /// co-engage radius, give them complementary jobs so they do not all freeze or
    /// bum-rush. Death-Rush is counted as present but never assigned a job and never
    /// pulled off Charge. Role changes wait out <see cref="DefaultRoleDwellSeconds"/>.
    /// </summary>
    public static class TheaterCommander
    {
        public const float DefaultCoEngageRadius = 48f;
        public const float DefaultRoleDwellSeconds = 2.5f;

        /// <summary>Meters to the formation's right. Pin stays on the axis.</summary>
        public const float FlankLateralMeters = 12f;
        public const float HarassLateralMeters = -8f;

        /// <summary>Outside this, Harass closes (Flank) instead of kiting in place.</summary>
        public const float HarassPocketMeters = 18f;

        public static float LateralBias(TheaterRole role)
        {
            switch (role)
            {
                case TheaterRole.Flank:
                    return FlankLateralMeters;
                case TheaterRole.Harass:
                    return HarassLateralMeters;
                default:
                    return 0f;
            }
        }

        public static float CoEngageRadius()
        {
            var v = PluginConfig.TheaterCoEngageRadius?.Value ?? DefaultCoEngageRadius;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 2f)
                return DefaultCoEngageRadius;
            return v;
        }

        public static float RoleDwellSeconds()
        {
            var v = PluginConfig.TheaterRoleDwellSeconds?.Value ?? DefaultRoleDwellSeconds;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
                return 0f;
            return v;
        }

        /// <summary>
        /// Writes <see cref="SquadRuntimeState.TheaterRole"/> for this tick.
        /// Call before the commander proposes orders.
        /// </summary>
        public static void Assign(IReadOnlyList<TheaterSquadView>? squads, float dt)
        {
            if (squads == null || squads.Count == 0)
                return;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f)
                dt = 0f;

            var radius = CoEngageRadius();
            var dwell = RoleDwellSeconds();
            var participants = new List<TheaterSquadView>(squads.Count);

            for (int i = 0; i < squads.Count; i++)
            {
                var view = squads[i];
                if (view?.State == null)
                    continue;

                if (!view.HasFocus || Horizontal(view.Centroid, view.FocusPosition) > radius)
                {
                    ClearRole(view.State);
                    continue;
                }

                if (IsDeathRush(view.DoctrineId))
                {
                    // Present for the 2+ gate, but never demoted off the bee-line.
                    ClearRole(view.State);
                    participants.Add(view);
                    continue;
                }

                if (view.State.TheaterRole != TheaterRole.None)
                    view.State.TheaterRoleAgeSeconds += dt;
                participants.Add(view);
            }

            var groups = new Dictionary<long, List<TheaterSquadView>>();
            for (int i = 0; i < participants.Count; i++)
            {
                var view = participants[i];
                if (!groups.TryGetValue(view.FocusPlayerId, out var list))
                {
                    list = new List<TheaterSquadView>();
                    groups[view.FocusPlayerId] = list;
                }
                list.Add(view);
            }

            foreach (var group in groups.Values)
            {
                if (group.Count < 2)
                {
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (!IsDeathRush(group[i].DoctrineId))
                            ClearRole(group[i].State);
                    }
                    continue;
                }

                var eligible = new List<TheaterSquadView>();
                for (int i = 0; i < group.Count; i++)
                {
                    if (!IsDeathRush(group[i].DoctrineId))
                        eligible.Add(group[i]);
                }

                if (eligible.Count == 0)
                    continue;

                var focusId = group[0].FocusPlayerId;
                Dictionary<TheaterSquadView, TheaterRole> ideal;
                if (eligible.Count == 1)
                {
                    ideal = new Dictionary<TheaterSquadView, TheaterRole>
                    {
                        [eligible[0]] = IsSkirmish(eligible[0].DoctrineId)
                            ? TheaterRole.Harass
                            : TheaterRole.Pin,
                    };
                }
                else
                {
                    ideal = ComputeIdeal(eligible);
                }

                CommitWithHysteresis(eligible, ideal, focusId, dwell);
            }
        }

        /// <summary>
        /// Map a doctrine order onto the theater job. Death-Rush and broken packs
        /// are returned unchanged. Pin on a line doctrine keeps its cadence.
        /// </summary>
        public static DoctrineOrderKind ShapeOrder(SquadSnapshot? snapshot, DoctrineOrderKind doctrineKind)
        {
            if (snapshot == null || snapshot.TheaterRole == TheaterRole.None)
                return doctrineKind;
            // Bee-line. A theater job must not peel Death-Rush onto Kite/Flank/Hold.
            if (IsDeathRush(snapshot.DoctrineId))
                return snapshot.ThreatCount > 0 ? DoctrineOrderKind.Charge : doctrineKind;
            if (snapshot.IsBroken || doctrineKind == DoctrineOrderKind.RetreatAndReform)
                return doctrineKind;

            switch (snapshot.TheaterRole)
            {
                case TheaterRole.Pin:
                    if (IsLine(snapshot.DoctrineId))
                        return doctrineKind;
                    if (doctrineKind == DoctrineOrderKind.Advance
                        || doctrineKind == DoctrineOrderKind.Charge
                        || doctrineKind == DoctrineOrderKind.Hold
                        || doctrineKind == DoctrineOrderKind.FocusFire
                        || doctrineKind == DoctrineOrderKind.ProtectMissiles)
                        return doctrineKind;
                    return DoctrineOrderKind.Advance;

                case TheaterRole.Flank:
                    return DoctrineOrderKind.Flank;

                case TheaterRole.Harass:
                    var pocket = HarassPocket();
                    if (float.IsNaN(snapshot.NearestThreatDistance)
                        || float.IsInfinity(snapshot.NearestThreatDistance)
                        || snapshot.NearestThreatDistance > pocket)
                        return DoctrineOrderKind.Flank;
                    return DoctrineOrderKind.Kite;

                default:
                    return doctrineKind;
            }
        }

        private static float HarassPocket()
        {
            var v = PluginConfig.AmbushOuterPocket?.Value ?? HarassPocketMeters;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 2f)
                return HarassPocketMeters;
            return v;
        }

        private static Dictionary<TheaterSquadView, TheaterRole> ComputeIdeal(List<TheaterSquadView> eligible)
        {
            var sorted = new List<TheaterSquadView>(eligible);
            sorted.Sort(CompareCloser);

            TheaterSquadView? pin = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (IsLine(sorted[i].DoctrineId))
                {
                    pin = sorted[i];
                    break;
                }
            }

            if (pin == null)
            {
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (!IsSkirmish(sorted[i].DoctrineId))
                    {
                        pin = sorted[i];
                        break;
                    }
                }
            }

            TheaterSquadView? harass = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (pin != null && ReferenceEquals(sorted[i], pin))
                    continue;
                if (IsSkirmish(sorted[i].DoctrineId))
                {
                    harass = sorted[i];
                    break;
                }
            }

            if (harass == null && sorted.Count >= 3)
            {
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    if (pin != null && ReferenceEquals(sorted[i], pin))
                        continue;
                    harass = sorted[i];
                    break;
                }
            }

            var ideal = new Dictionary<TheaterSquadView, TheaterRole>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var view = sorted[i];
                if (pin != null && ReferenceEquals(view, pin))
                    ideal[view] = TheaterRole.Pin;
                else if (harass != null && ReferenceEquals(view, harass))
                    ideal[view] = TheaterRole.Harass;
                else
                    ideal[view] = TheaterRole.Flank;
            }

            return ideal;
        }

        private static void CommitWithHysteresis(
            List<TheaterSquadView> eligible,
            Dictionary<TheaterSquadView, TheaterRole> ideal,
            long focusId,
            float dwell)
        {
            var kept = new Dictionary<TheaterSquadView, TheaterRole>();
            for (int i = 0; i < eligible.Count; i++)
            {
                var view = eligible[i];
                var state = view.State;
                if (state.TheaterRole == TheaterRole.None)
                    continue;
                if (!state.HasTheaterFocus || state.TheaterFocusId != focusId)
                    continue;
                // Age was already advanced this call. Releasing at dwell avoids a same-tick swap-back.
                if (state.TheaterRoleAgeSeconds + 0.001f < dwell)
                    kept[view] = state.TheaterRole;
            }

            DropDuplicateUnique(kept, TheaterRole.Pin);
            DropDuplicateUnique(kept, TheaterRole.Harass);

            var pinTaken = false;
            var harassTaken = false;
            foreach (var role in kept.Values)
            {
                if (role == TheaterRole.Pin)
                    pinTaken = true;
                else if (role == TheaterRole.Harass)
                    harassTaken = true;
            }

            var ordered = new List<TheaterSquadView>(eligible);
            ordered.Sort(CompareCloser);
            for (int i = 0; i < ordered.Count; i++)
            {
                var view = ordered[i];
                TheaterRole role;
                if (kept.TryGetValue(view, out var sticky))
                {
                    role = sticky;
                }
                else
                {
                    role = ideal.TryGetValue(view, out var want) ? want : TheaterRole.Flank;
                    if (role == TheaterRole.Pin && pinTaken)
                        role = !harassTaken && IsSkirmish(view.DoctrineId) ? TheaterRole.Harass : TheaterRole.Flank;
                    else if (role == TheaterRole.Harass && harassTaken)
                        role = TheaterRole.Flank;

                    if (role == TheaterRole.Pin)
                        pinTaken = true;
                    else if (role == TheaterRole.Harass)
                        harassTaken = true;
                }

                Commit(view, role, focusId);
            }
        }

        /// <summary>One unique job (Pin or Harass). The oldest holder wins; ties break on StableId.</summary>
        private static void DropDuplicateUnique(Dictionary<TheaterSquadView, TheaterRole> kept, TheaterRole unique)
        {
            TheaterSquadView? best = null;
            var bestAge = float.NegativeInfinity;
            foreach (var kv in kept)
            {
                if (kv.Value != unique)
                    continue;
                var age = kv.Key.State.TheaterRoleAgeSeconds;
                if (best == null
                    || age > bestAge + 0.0001f
                    || (Math.Abs(age - bestAge) <= 0.0001f && string.Compare(StableOf(kv.Key), StableOf(best), StringComparison.Ordinal) < 0))
                {
                    best = kv.Key;
                    bestAge = age;
                }
            }

            if (best == null)
                return;

            var drop = new List<TheaterSquadView>();
            foreach (var kv in kept)
            {
                if (kv.Value == unique && !ReferenceEquals(kv.Key, best))
                    drop.Add(kv.Key);
            }

            for (int i = 0; i < drop.Count; i++)
                kept.Remove(drop[i]);
        }

        private static void Commit(TheaterSquadView view, TheaterRole role, long focusId)
        {
            var state = view.State;
            var changed = state.TheaterRole != role
                          || !state.HasTheaterFocus
                          || state.TheaterFocusId != focusId;
            state.TheaterRole = role;
            state.TheaterFocusId = focusId;
            state.TheaterFocusPosition = view.FocusPosition;
            state.HasTheaterFocus = true;
            if (changed)
                state.TheaterRoleAgeSeconds = 0f;
        }

        private static void ClearRole(SquadRuntimeState state)
        {
            state.TheaterRole = TheaterRole.None;
            state.TheaterRoleAgeSeconds = 0f;
            state.HasTheaterFocus = false;
            state.TheaterFocusId = 0;
        }

        private static int CompareCloser(TheaterSquadView a, TheaterSquadView b)
        {
            var da = Horizontal(a.Centroid, a.FocusPosition);
            var db = Horizontal(b.Centroid, b.FocusPosition);
            var cmp = da.CompareTo(db);
            if (cmp != 0)
                return cmp;
            return string.Compare(StableOf(a), StableOf(b), StringComparison.Ordinal);
        }

        private static string StableOf(TheaterSquadView view)
        {
            if (!string.IsNullOrEmpty(view.StableId))
                return view.StableId;
            return view.State?.StableId ?? "";
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        internal static bool IsDeathRush(string? doctrineId)
            => Eq(doctrineId, "death-rush");

        private static bool IsLine(string? doctrineId)
            => Eq(doctrineId, "roman") || Eq(doctrineId, "viking-shieldwall");

        private static bool IsSkirmish(string? doctrineId)
            => Eq(doctrineId, "ambush")
               || Eq(doctrineId, "steppe")
               || Eq(doctrineId, "pack-hunters")
               || Eq(doctrineId, "insect-siege");

        private static bool Eq(string? a, string b)
            => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
