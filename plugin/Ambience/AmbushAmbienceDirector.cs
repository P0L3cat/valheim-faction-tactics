using System;
using System.Collections.Generic;
using FactionTactics.Config;
using FactionTactics.Squad;
#if VALHEIM_REFS
using UnityEngine;
#endif

namespace FactionTactics.Ambience
{
    /// <summary>
    /// TEMP Black Forest ambush wire: force a foggier EnvMan environment and show a
    /// one-shot center message ("the hair on your neck stands up") when a qualifying
    /// Ambush (Greydwarf) squad is near a player.
    ///
    /// Disable with <c>EnableAmbushAmbienceTemp=false</c> for proper silent ambush.
    /// EnvMan / MessageHud calls are VALHEIM_REFS-only; stub Tick is a no-op.
    /// </summary>
    public sealed class AmbushAmbienceDirector
    {
        public const string AmbushDoctrineId = "ambush";

        private bool _forced;
#if VALHEIM_REFS
        private bool _loggedActivation;
        private bool _loggedInvalidEnv;
        private readonly Dictionary<long, float> _lastMessageAt = new Dictionary<long, float>();
#endif

        /// <summary>
        /// Called from <see cref="SquadDirector.Tick"/> after squads are processed.
        /// </summary>
        public void Tick(IReadOnlyList<SquadUnit> active)
        {
            if (PluginConfig.EnableAmbushAmbienceTemp?.Value != true)
            {
                ClearForcedEnv();
                return;
            }

#if VALHEIM_REFS
            TickLive(active);
#else
            // Stub / CI: no EnvMan or players. Keep the method so SquadDirector always calls it.
            _ = active;
            ClearForcedEnv();
#endif
        }

        /// <summary>True while this director currently holds a forced EnvMan environment.</summary>
        public bool IsForcingEnvironment => _forced;

#if VALHEIM_REFS
        private void TickLive(IReadOnlyList<SquadUnit> active)
        {
            var minSize = PluginConfig.AmbushAmbienceMinSquadSize?.Value ?? 3;
            var range = PluginConfig.AmbushAmbiencePlayerRange?.Value ?? 40f;
            var threatNearPlayer = false;

            List<Player>? players = null;
            try
            {
                players = CollectPlayers();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"AmbushAmbienceTemp: player scan failed: {ex.Message}");
            }

            if (players == null || players.Count == 0 || active == null || active.Count == 0)
            {
                ClearForcedEnv();
                return;
            }

            foreach (var squad in active)
            {
                if (!IsQualifyingAmbush(squad, minSize, out var centroid))
                    continue;

                foreach (var player in players)
                {
                    if (player == null)
                        continue;
                    try
                    {
                        if (player.IsDead())
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    if (!IsPlayerNearSquad(player, squad, centroid, range))
                        continue;
                    if (!PassesBiomeGate(player))
                        continue;

                    threatNearPlayer = true;
                    MaybeMessagePlayer(player);
                }
            }

            if (threatNearPlayer)
                EnsureForcedEnv();
            else
                ClearForcedEnv();
        }

        private static List<Player> CollectPlayers()
        {
            // Dedicated: Character.IsPlayer via maintained Character lists (not GetAllPlayers alone).
            return FactionTactics.Util.ValheimWorldScan.CollectPlayers();
        }

        private static bool IsQualifyingAmbush(SquadUnit squad, int minSize, out Vector3 centroid)
        {
            centroid = Vector3.zero;
            if (squad?.Doctrine == null)
                return false;
            if (!string.Equals(squad.Doctrine.Id, AmbushDoctrineId, StringComparison.OrdinalIgnoreCase))
                return false;

            int alive = 0;
            Vector3 sum = Vector3.zero;
            foreach (var m in squad.Members)
            {
                if (m == null || !m.IsAlive)
                    continue;
                if (m.HealthRatio >= 0f && m.HealthRatio <= 0.02f)
                    continue;
                alive++;
                sum += m.Position;
            }

            if (alive < minSize)
                return false;
            centroid = new Vector3(sum.x / alive, sum.y / alive, sum.z / alive);
            return true;
        }

        private static bool IsPlayerNearSquad(Player player, SquadUnit squad, Vector3 centroid, float range)
        {
            Vector3 pos;
            try
            {
                pos = player.transform.position;
            }
            catch
            {
                return false;
            }

            if (Vector3.Distance(pos, centroid) <= range)
                return true;

            foreach (var m in squad.Members)
            {
                if (m == null || !m.IsAlive)
                    continue;
                if (Vector3.Distance(pos, m.Position) <= range)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Prefer Black Forest; if biome lookup fails, still fire so edge greys work.
        /// </summary>
        private static bool PassesBiomeGate(Player player)
        {
            try
            {
                return player.GetCurrentBiome() == Heightmap.Biome.BlackForest;
            }
            catch
            {
                return true;
            }
        }

        private void MaybeMessagePlayer(Player player)
        {
            var text = PluginConfig.AmbushAmbienceMessage?.Value;
            if (string.IsNullOrEmpty(text))
                return;

            var cooldown = PluginConfig.AmbushAmbienceMessageCooldownSeconds?.Value ?? 90f;
            if (cooldown < 0f)
                cooldown = 0f;

            long id = 0;
            try
            {
                id = player.GetPlayerID();
                if (id == 0)
                    id = player.GetInstanceID();
            }
            catch
            {
                try { id = player.GetInstanceID(); }
                catch { return; }
            }

            var now = Time.time;
            if (_lastMessageAt.TryGetValue(id, out var last) && now - last < cooldown)
                return;

            try
            {
                player.Message(MessageHud.MessageType.Center, text);
                _lastMessageAt[id] = now;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"AmbushAmbienceTemp: player.Message failed: {ex.Message}");
            }
        }

        private void EnsureForcedEnv()
        {
            var env = PluginConfig.AmbushAmbienceFogEnvironment?.Value;
            if (string.IsNullOrEmpty(env))
            {
                // Empty = skip fog (still clear a previous force if we held one).
                ClearForcedEnv();
                return;
            }

            if (_forced)
                return;

            try
            {
                if (EnvMan.instance == null)
                    return;
                EnvMan.instance.SetForceEnvironment(env);
                _forced = true;
                if (!_loggedActivation)
                {
                    Plugin.Log?.LogInfo(
                        $"AmbushAmbienceTemp: forcing env '{env}' near Ambush squad (disable EnableAmbushAmbienceTemp for silent ambush).");
                    _loggedActivation = true;
                }
                else if (PluginConfig.DebugLogging?.Value == true)
                {
                    Plugin.Log?.LogDebug($"AmbushAmbienceTemp: re-forcing env '{env}'.");
                }
            }
            catch (Exception ex)
            {
                if (!_loggedInvalidEnv)
                {
                    Plugin.Log?.LogWarning(
                        $"AmbushAmbienceTemp: SetForceEnvironment('{env}') failed (invalid env name?): {ex.Message}");
                    _loggedInvalidEnv = true;
                }
            }
        }
#endif

        private void ClearForcedEnv()
        {
            if (!_forced)
                return;
#if VALHEIM_REFS
            try
            {
                if (EnvMan.instance != null)
                    EnvMan.instance.SetForceEnvironment("");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"AmbushAmbienceTemp: clear force env failed: {ex.Message}");
            }
#endif
            _forced = false;
        }
    }
}
