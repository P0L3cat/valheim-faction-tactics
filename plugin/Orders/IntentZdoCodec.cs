using System;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Pure pack/unpack for <see cref="MemberIntent"/> ↔ ZDO ints (unit-testable without Valheim).
    /// Schema v3 for hybrid commander→executor replication via <c>IntentZdoSync</c>.
    /// </summary>
    public static class IntentZdoCodec
    {
        public const int SchemaVersion = 3;

        /// <summary>Max age (seconds) before a replicated intent is treated as stale.</summary>
        public const float StaleAfterSeconds = 6f;

        [Flags]
        public enum IntentFlags : int
        {
            None = 0,
            Active = 1 << 0,
            HoldGround = 1 << 1,
            PreferRun = 1 << 2,
            AllowVanillaChase = 1 << 3,
            PreferKeepRange = 1 << 4,
            AssaultWallBreaker = 1 << 5,
            AssaultMissileCover = 1 << 6,
            AllowVanillaStructure = 1 << 7,
            DeathRush = 1 << 8,
        }

        public static IntentFlags PackFlags(MemberIntent intent)
        {
            var f = IntentFlags.Active;
            if (intent.HoldGround) f |= IntentFlags.HoldGround;
            if (intent.PreferRun) f |= IntentFlags.PreferRun;
            if (intent.AllowVanillaChase) f |= IntentFlags.AllowVanillaChase;
            if (intent.PreferKeepRange) f |= IntentFlags.PreferKeepRange;
            if (intent.AssaultWallBreaker) f |= IntentFlags.AssaultWallBreaker;
            if (intent.AssaultMissileCover) f |= IntentFlags.AssaultMissileCover;
            if (intent.AllowVanillaStructure) f |= IntentFlags.AllowVanillaStructure;
            if (intent.DeathRush) f |= IntentFlags.DeathRush;
            return f;
        }

        public static void ApplyFlags(MemberIntent intent, IntentFlags flags)
        {
            intent.HoldGround = (flags & IntentFlags.HoldGround) != 0;
            intent.PreferRun = (flags & IntentFlags.PreferRun) != 0;
            intent.AllowVanillaChase = (flags & IntentFlags.AllowVanillaChase) != 0;
            intent.PreferKeepRange = (flags & IntentFlags.PreferKeepRange) != 0;
            intent.AssaultWallBreaker = (flags & IntentFlags.AssaultWallBreaker) != 0;
            intent.AssaultMissileCover = (flags & IntentFlags.AssaultMissileCover) != 0;
            intent.AllowVanillaStructure = (flags & IntentFlags.AllowVanillaStructure) != 0;
            intent.DeathRush = (flags & IntentFlags.DeathRush) != 0;
        }

        public static bool IsActive(IntentFlags flags) => (flags & IntentFlags.Active) != 0;

        public static bool IsFresh(float writtenAtSeconds, float nowSeconds, float staleAfter = StaleAfterSeconds)
        {
            if (writtenAtSeconds <= 0f)
                return false;
            var age = nowSeconds - writtenAtSeconds;
            if (age < -2f)
                return true;
            return age >= 0f && age <= staleAfter;
        }
    }
}
