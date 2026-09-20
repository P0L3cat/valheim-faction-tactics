namespace FactionTactics.Combat
{
    /// <summary>
    /// Executor-side tuning shared by server listen-host and FactionTactics.Client.
    /// Defaults match PluginConfig binds; server copies ConfigEntry values here on Bind/set.
    /// Clients apply values from FT_Config sync / reply so HoldAttackCooldown reaches owners.
    /// </summary>
    public static class CombatTuning
    {
        public static float HoldAttackCooldown = 0.85f;
        public static float HoldAttackRangeFactor = 1.0f;
        public static float SwingStaggerMs = 75f;

        public static void ApplyKnob(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null)
                return;
            key = key.Trim();
            if (!float.TryParse(value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var f))
                return;

            if (Matches(key, "HoldAttackCooldown"))
                HoldAttackCooldown = Clamp(f, 0.05f, 10f);
            else if (Matches(key, "HoldAttackRangeFactor"))
                HoldAttackRangeFactor = Clamp(f, 0.25f, 3f);
            else if (Matches(key, "SwingStaggerMs"))
                SwingStaggerMs = Clamp(f, 0f, 2000f);
        }

        public static void CopyFromPluginConfig()
        {
#if !FT_CLIENT
            try
            {
                if (FactionTactics.Config.PluginConfig.HoldAttackCooldown != null)
                    HoldAttackCooldown = FactionTactics.Config.PluginConfig.HoldAttackCooldown.Value;
                if (FactionTactics.Config.PluginConfig.HoldAttackRangeFactor != null)
                    HoldAttackRangeFactor = FactionTactics.Config.PluginConfig.HoldAttackRangeFactor.Value;
                if (FactionTactics.Config.PluginConfig.SwingStaggerMs != null)
                    SwingStaggerMs = FactionTactics.Config.PluginConfig.SwingStaggerMs.Value;
            }
            catch
            {
                // Config not bound yet
            }
#endif
        }

        private static bool Matches(string key, string name)
            => string.Equals(key, name, System.StringComparison.OrdinalIgnoreCase);

        private static float Clamp(float v, float lo, float hi)
            => v < lo ? lo : (v > hi ? hi : v);
    }
}
