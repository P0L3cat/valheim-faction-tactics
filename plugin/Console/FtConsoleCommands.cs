using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using FactionTactics.Config;
using FactionTactics.Orders;
using FactionTactics.Squad;

#if !VALHEIM_REFS
using ZNet = FactionTactics.Stubs.ZNet;
#endif

namespace FactionTactics.ConsoleCmds
{
    /// <summary>
    /// Admin Terminal knobs: <c>ft help|get|set|reload|status</c>.
    /// Registered on the server/listen-host plugin. Setting ConfigEntry.Value
    /// takes effect next SquadDirector tick (hot knobs read .Value every tick).
    /// </summary>
    public static class FtConsoleCommands
    {
        public const string CommandName = "ft";
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;

#if VALHEIM_REFS
            _ = new Terminal.ConsoleCommand(
                CommandName,
                "Faction Tactics knobs: ft help | get <key> | set <key> <value> | reload | status",
                (Terminal.ConsoleEventArgs args) => Run(args),
                isCheat: false,
                isNetwork: false,
                onlyServer: true,
                isSecret: false,
                allowInDevBuild: true,
                hideBehindDevCommands: false,
                optionsFetcher: TabOptions,
                alwaysRefreshTabOptions: true,
                remoteCommand: false,
                onlyAdmin: true);
            Plugin.Log?.LogInfo("Faction Tactics console: registered 'ft' (admin / dedicated / listen-host).");
#else
            Plugin.Log?.LogInfo("Faction Tactics console: skipped (no VALHEIM_REFS).");
#endif
        }

#if VALHEIM_REFS
        private static List<string> TabOptions()
        {
            var list = new List<string> { "help", "get", "set", "reload", "status" };
            list.AddRange(PluginConfig.KnobKeys);
            return list;
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            if (!IsServerAdmin())
            {
                args.Context.AddString("[ft] admin/server only");
                return;
            }

            if (args.Length < 2)
            {
                PrintHelp(args);
                return;
            }

            var sub = args[1].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                case "?":
                    PrintHelp(args);
                    break;
                case "get":
                    CmdGet(args);
                    break;
                case "set":
                    CmdSet(args);
                    break;
                case "reload":
                    CmdReload(args);
                    break;
                case "status":
                    CmdStatus(args);
                    break;
                default:
                    args.Context.AddString($"[ft] unknown subcommand '{args[1]}' — try: ft help");
                    break;
            }
        }

        private static bool IsServerAdmin()
        {
            if (ZNet.instance == null)
                return false;
            if (!ZNet.instance.IsServer())
                return false;
            // Dedicated console and listen-host server process: LocalPlayerIsAdminOrHost is true on server.
            try
            {
                return ZNet.instance.LocalPlayerIsAdminOrHost();
            }
            catch
            {
                return true;
            }
        }

        private static void PrintHelp(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString($"[ft] Faction Tactics {FtVersion.ProductVersion} (schema v{FtVersion.IntentSchemaVersion})");
            args.Context.AddString("  ft help                 — this list");
            args.Context.AddString("  ft get <key>            — read ConfigEntry");
            args.Context.AddString("  ft set <key> <value>    — set + persist config");
            args.Context.AddString("  ft reload               — reload BepInEx cfg from disk");
            args.Context.AddString("  ft status               — version / heartbeat / squads");
            args.Context.AddString("Keys:");
            foreach (var line in PluginConfig.FormatKnobHelpLines())
                args.Context.AddString("  " + line);
        }

        private static void CmdGet(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3)
            {
                args.Context.AddString("[ft] usage: ft get <key>");
                return;
            }

            if (!PluginConfig.TryGetKnob(args[2], out var entry) || entry == null)
            {
                args.Context.AddString($"[ft] unknown key '{args[2]}' — ft help");
                return;
            }

            args.Context.AddString($"[ft] {entry.Definition.Key} = {FormatValue(entry)}  ({entry.Definition.Section})");
        }

        private static void CmdSet(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 4)
            {
                args.Context.AddString("[ft] usage: ft set <key> <value>");
                return;
            }

            if (!PluginConfig.TryGetKnob(args[3 - 1], out var entry) || entry == null)
            {
                args.Context.AddString($"[ft] unknown key '{args[2]}' — ft help");
                return;
            }

            // Join remaining tokens so string knobs can contain spaces.
            var raw = string.Join(" ", args.Args.Skip(3));
            var before = FormatValue(entry);
            try
            {
                if (!TryAssign(entry, raw))
                {
                    args.Context.AddString($"[ft] could not parse '{raw}' as {entry.SettingType.Name}");
                    return;
                }
            }
            catch (Exception ex)
            {
                args.Context.AddString($"[ft] set failed: {ex.Message}");
                return;
            }

            try
            {
                PluginConfig.File?.Save();
            }
            catch (Exception ex)
            {
                args.Context.AddString($"[ft] value set but Save failed: {ex.Message}");
            }

            args.Context.AddString($"[ft] {entry.Definition.Key}: {before} → {FormatValue(entry)} (persisted)");
            Plugin.Log?.LogInfo($"[ft set] {entry.Definition.Key}={FormatValue(entry)} (was {before})");
        }

        private static void CmdReload(Terminal.ConsoleEventArgs args)
        {
            var file = PluginConfig.File;
            if (file == null)
            {
                args.Context.AddString("[ft] no ConfigFile bound");
                return;
            }

            try
            {
                file.Reload();
                args.Context.AddString($"[ft] reloaded {file.ConfigFilePath}");
                Plugin.Log?.LogInfo($"[ft reload] {file.ConfigFilePath}");
            }
            catch (Exception ex)
            {
                args.Context.AddString($"[ft] reload failed: {ex.Message}");
            }
        }

        private static void CmdStatus(Terminal.ConsoleEventArgs args)
        {
            foreach (var line in BuildStatusLines())
                args.Context.AddString(line);
        }

        internal static IEnumerable<string> BuildStatusLines()
        {
            yield return $"[ft] Faction Tactics {Plugin.PluginVersion} product={FtVersion.ProductVersion} schema=v{FtVersion.IntentSchemaVersion}";
            yield return $"[ft] EnablePlugin={PluginConfig.EnablePlugin?.Value} tick={PluginConfig.TickIntervalSeconds?.Value}s " +
                         $"zdoIntent={PluginConfig.EnableZdoIntentSync?.Value} sticky={PluginConfig.EnableStickyEnemyOwnership?.Value} " +
                         $"enemyOwn={PluginConfig.EnableEnemyServerOwnership?.Value}";

            var director = Plugin.Instance?.Director;
            if (director != null)
            {
                foreach (var line in director.FormatStatusLines())
                    yield return "[ft] " + line;
            }
            else
            {
                yield return "[ft] SquadDirector: (not running — EnablePlugin false?)";
            }
        }

        private static bool TryAssign(ConfigEntryBase entry, string raw)
        {
            var t = entry.SettingType;
            if (t == typeof(bool))
            {
                if (!TryParseBool(raw, out var b))
                    return false;
                entry.BoxedValue = b;
                return true;
            }

            if (t == typeof(int))
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return false;
                entry.BoxedValue = i;
                return true;
            }

            if (t == typeof(float))
            {
                if (!float.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return false;
                entry.BoxedValue = f;
                return true;
            }

            if (t == typeof(string))
            {
                entry.BoxedValue = raw;
                return true;
            }

            // Fallback: BepInEx Toml converter
            entry.SetSerializedValue(raw);
            return true;
        }

        private static bool TryParseBool(string raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                case "y":
                    value = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                case "n":
                    value = false;
                    return true;
                default:
                    return bool.TryParse(raw, out value);
            }
        }

        private static string FormatValue(ConfigEntryBase entry)
        {
            var v = entry.BoxedValue;
            if (v == null)
                return "null";
            if (v is float f)
                return f.ToString("G", CultureInfo.InvariantCulture);
            if (v is bool b)
                return b ? "true" : "false";
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }
#endif
    }
}
