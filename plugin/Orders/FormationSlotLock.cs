using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Doctrine;
using FactionTactics.Squad;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Phase A: lock instanceId→slotIndex until order change, reshuffle timer, or casualty delta.
    /// Leaves holes for dead members (no equal-spacing rebuild every death).
    /// </summary>
    public static class FormationSlotLock
    {
        public static long SlotLocks { get; private set; }
        public static long Reshuffles { get; private set; }

        public static void ResetCounters()
        {
            SlotLocks = Reshuffles = 0;
        }

        /// <summary>
        /// Ensure living members have stable slot indices. Returns capacity (maxIndex+1) for spacing.
        /// </summary>
        public static int EnsureSlots(
            SquadRuntimeState state,
            SquadUnit squad,
            SquadOrder order,
            float casualtyRatio)
        {
            if (state == null || squad == null || order == null)
                return Math.Max(1, CountAlive(squad));

            var reshuffleSec = PluginConfig.FormationReshuffleSeconds?.Value ?? 3f;
            var casualtyThresh = PluginConfig.FormationCasualtyReshuffle?.Value ?? 0.25f;

            var orderChanged = state.SlotLockOrderKind != order.OrderKind
                               || state.SlotLockFormation != order.Formation;
            var timerExpired = state.SlotLockAgeSeconds >= reshuffleSec;
            var casualtyDelta = Math.Abs(casualtyRatio - state.SlotLockCasualtyRatio) >= casualtyThresh;
            var empty = state.LockedSlots.Count == 0;

            if (empty || orderChanged || timerExpired || casualtyDelta)
            {
                Reshuffle(state, squad, order, casualtyRatio);
                Reshuffles++;
            }
            else
            {
                // Assign newcomers into holes / append without regridding survivors.
                AssignNewcomers(state, squad);
            }

            // Gauge: living members currently holding a locked index (snapshot, not cumulative spam).
            int lockedLiving = 0;
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                if (state.LockedSlots.ContainsKey(m.InstanceId))
                    lockedLiving++;
            }
            SlotLocks = lockedLiving;

            return Math.Max(1, state.SlotLockCapacity);
        }

        public static bool TryGetSlot(SquadRuntimeState? state, long instanceId, out int slotIndex)
        {
            slotIndex = 0;
            if (state == null)
                return false;
            return state.LockedSlots.TryGetValue(instanceId, out slotIndex);
        }

        public static void TickAge(SquadRuntimeState state, float dt)
        {
            if (state == null)
                return;
            state.SlotLockAgeSeconds += dt;
        }

        private static void Reshuffle(
            SquadRuntimeState state,
            SquadUnit squad,
            SquadOrder order,
            float casualtyRatio)
        {
            state.LockedSlots.Clear();
            var living = new List<SquadMemberView>();
            foreach (var m in squad.Members)
            {
                if (m.IsAlive)
                    living.Add(m);
            }
            living.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));

            for (int i = 0; i < living.Count; i++)
                state.LockedSlots[living[i].InstanceId] = i;

            state.SlotLockCapacity = Math.Max(1, living.Count);
            state.SlotLockOrderKind = order.OrderKind;
            state.SlotLockFormation = order.Formation;
            state.SlotLockCasualtyRatio = casualtyRatio;
            state.SlotLockAgeSeconds = 0f;
        }

        private static void AssignNewcomers(SquadRuntimeState state, SquadUnit squad)
        {
            var used = new HashSet<int>(state.LockedSlots.Values);
            // Drop dead from active consideration but keep their indices as holes
            // (do not remove keys — holes stay until reshuffle).
            foreach (var m in squad.Members)
            {
                if (!m.IsAlive)
                    continue;
                if (state.LockedSlots.ContainsKey(m.InstanceId))
                    continue;

                int free = 0;
                while (used.Contains(free))
                    free++;
                state.LockedSlots[m.InstanceId] = free;
                used.Add(free);
                if (free + 1 > state.SlotLockCapacity)
                    state.SlotLockCapacity = free + 1;
            }
        }

        private static int CountAlive(SquadUnit squad)
        {
            int n = 0;
            foreach (var m in squad.Members)
            {
                if (m.IsAlive)
                    n++;
            }
            return n;
        }
    }
}
