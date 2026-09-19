using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FactionTactics.Tests
{
    /// <summary>
    /// HARD INVARIANT: Faction Tactics must never call spawn / raid / RandomEvent APIs.
    /// </summary>
    public class InvariantTests
    {
        // Forbidden API call shapes (not documentation mentioning the invariant).
        static readonly Regex[] ForbiddenCalls =
        {
            new Regex(@"\bRandomEventSystem\b", RegexOptions.Compiled),
            new Regex(@"\bRandEventSystem\b", RegexOptions.Compiled),
            new Regex(@"\bStartRandomEvent\b", RegexOptions.Compiled),
            new Regex(@"\bSpawnSystem\b", RegexOptions.Compiled),
            new Regex(@"\bSpawnCreature\b", RegexOptions.Compiled),
            new Regex(@"\bEnemyHud\.instance\.ShowRaid\b", RegexOptions.Compiled),
            new Regex(@"\bRaidEvent\b", RegexOptions.Compiled),
            new Regex(@"\b\.Spawn\s*\(", RegexOptions.Compiled),
        };

        // Lines that document / forbid the APIs are allowed.
        static readonly Regex AllowedContext = new Regex(
            @"HARD INVARIANT|never spawn|no Defense/Raid|RandomEvents?/raids|encounter-rate|Assault only|Raid Event",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [Fact]
        public void Plugin_sources_have_zero_spawn_raid_RandomEvent_API_calls()
        {
            var pluginRoot = FindPluginRoot();
            var files = Directory.GetFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .ToList();

            Assert.NotEmpty(files);

            var violations = new System.Collections.Generic.List<string>();
            foreach (var file in files)
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (AllowedContext.IsMatch(line))
                        continue;
                    // Strip // comments for call detection
                    var code = line;
                    var commentIdx = code.IndexOf("//", StringComparison.Ordinal);
                    if (commentIdx >= 0)
                        code = code.Substring(0, commentIdx);
                    if (string.IsNullOrWhiteSpace(code))
                        continue;

                    foreach (var rx in ForbiddenCalls)
                    {
                        if (rx.IsMatch(code))
                            violations.Add($"{Path.GetRelativePath(pluginRoot, file)}:{i + 1}: {line.Trim()}");
                    }
                }
            }

            Assert.True(violations.Count == 0,
                "Spawn/raid/RandomEvent API usage found:\n" + string.Join("\n", violations));
        }

        static string FindPluginRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "plugin");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "FactionTactics.csproj")))
                    return candidate;
                // tests run from tests/FactionTactics.Tests/bin/...
                var up = Path.Combine(dir.FullName, "..", "..", "..", "..", "plugin");
                up = Path.GetFullPath(up);
                if (Directory.Exists(up) && File.Exists(Path.Combine(up, "FactionTactics.csproj")))
                    return up;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate plugin/ directory from test base " + AppContext.BaseDirectory);
        }
    }
}
