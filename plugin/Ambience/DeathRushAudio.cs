using System;
using System.Collections.Generic;
using FactionTactics.Orders;
using FactionTactics.Util;

#if VALHEIM_REFS
using UnityEngine;
#endif

namespace FactionTactics.Ambience
{
    /// <summary>
    /// Plays vanilla Greyling alert / scream-like SFX on Death-Rush charge pulses
    /// via <see cref="BaseAI.Alert"/> + <c>m_alertedEffects</c> (no custom asset pack).
    /// Nate: use the greyling VO that sounds most like screaming.
    /// </summary>
    public static class DeathRushAudio
    {
        private static readonly Dictionary<int, float> LastPulseAt = new Dictionary<int, float>();
        private const float DefaultCooldown = 4.5f;

#if VALHEIM_REFS
        public static void PulseIfNeeded(MonsterAI ai, MemberIntent intent)
        {
            if (ai == null || intent == null)
                return;
            if (!intent.DeathRush)
                return;
            if (intent.OrderKind != Doctrine.DoctrineOrderKind.Charge)
                return;

            int id;
            try { id = ai.GetInstanceID(); }
            catch { return; }

            var now = Time.time;
            var cd = DefaultCooldown;
#if !FT_CLIENT
            try
            {
                var entry = FactionTactics.Config.PluginConfig.DeathRushScreamCooldownSeconds;
                if (entry != null && entry.Value > 0.5f)
                    cd = entry.Value;
            }
            catch { /* unbound */ }
#endif
            if (cd < 0.5f) cd = 0.5f;
            if (LastPulseAt.TryGetValue(id, out var last) && now - last < cd)
                return;
            LastPulseAt[id] = now;

            try
            {
                // Vanilla alert path fires m_alertedEffects (scream-ish Greyling VO).
                ai.Alert();
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"DeathRushAudio.Alert failed: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                var tr = HarmonyLib.Traverse.Create((BaseAI)ai);
                var effects = tr.Field("m_alertedEffects").GetValue<EffectList>();
                if (effects != null && effects.HasEffects())
                    effects.Create(ai.transform.position, ai.transform.rotation);
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"DeathRushAudio.m_alertedEffects failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
#else
        public static void PulseIfNeeded(object? ai, MemberIntent intent)
        {
            _ = ai;
            _ = intent;
        }
#endif
    }
}
