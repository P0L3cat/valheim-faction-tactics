#if VALHEIM_REFS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FactionTactics.Config;
using UnityEngine;

namespace FactionTactics.Util
{
    /// <summary>
    /// Dedicated-server-safe world scans (0.1.8).
    /// Critical findings from GPortal 0.1.6:
    /// <list type="bullet">
    /// <item><c>ZNet.GetAllCharacterZDOS()</c> returns ONLY player ZDOs — never mobs.</item>
    /// <item><c>Character.s_characters</c> / GetAllCharacters often empty on dedicated (chars=0 with players online).</item>
    /// <item><c>FindObjectsOfType&lt;MonsterAI&gt;</c> also yields 0 on dedicated.</item>
    /// </list>
    /// Mob discovery uses BaseAI.m_instances, ZNetScene.m_instances, ZDOMan prefab/sector walks,
    /// Resources.FindObjectsOfTypeAll, and Player.GetAllPlayers for anchors.
    /// </summary>
    public static class ValheimWorldScan
    {
        private static bool _loggedDedicatedDiscovery;
        private static bool _sceneDumpDone;
        private static float _lastSceneDumpRealtime;

        private static FieldInfo? _sCharactersField;
        private static bool _sCharactersResolved;
        private static FieldInfo? _baseAiInstancesField;
        private static bool _baseAiInstancesResolved;
        private static FieldInfo? _zNetSceneInstancesField;
        private static bool _zNetSceneInstancesFieldResolved;

        private static readonly HashSet<string> _loggedPathFailures =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly string[] KnownEnemyPrefabs =
        {
            "Skeleton", "Skeleton_Poison", "Skeleton_NoArcher", "Skeleton_Hildir",
            "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman",
            "Draugr", "Draugr_Elite", "Draugr_Ranged",
            "Goblin", "GoblinShaman", "GoblinBrute", "Fuling",
            "Wolf", "Drake", "Hatchling",
            "Blob", "BlobElite",
            "Seeker", "SeekerBrood", "Tick", "Gjall",
            "Charred_Melee", "Charred_Archer", "Charred_Mage", "Asksvin",
            "Troll",
        };

        /// <summary>Last Character.s_characters / GetAllCharacters size (often 0 on dedicated).</summary>
        public static int LastScanCharacterCount { get; private set; }

        /// <summary>Last scan player-anchor count.</summary>
        public static int LastScanPlayerCount { get; private set; }

        /// <summary>Non-player characters via GetCharactersInRange around anchors.</summary>
        public static int LastScanInRangeCount { get; private set; }

        /// <summary>
        /// Last <c>ZNet.GetAllCharacterZDOS</c> count — player ZDOs only (not mobs).
        /// Heartbeat: <c>playerZdos=</c>.
        /// </summary>
        public static int LastScanPlayerZdoCount { get; private set; }

        /// <summary>Obsolete alias — same as <see cref="LastScanPlayerZdoCount"/>.</summary>
        public static int LastScanZdoCount => LastScanPlayerZdoCount;

        /// <summary>ZDOs for which FindInstance returned a live view (prefab/sector paths).</summary>
        public static int LastScanInstancesFound { get; private set; }

        /// <summary>BaseAI.m_instances count (reflection).</summary>
        public static int LastScanBaseAiInstances { get; private set; }

        /// <summary>ZNetScene.m_instances.Count (reflection).</summary>
        public static int LastScanSceneInstances { get; private set; }

        /// <summary>ZNetScene.NrOfInstances() last scan.</summary>
        public static int LastScanSceneNrOfInstances { get; private set; }

        /// <summary>Enemy-prefab ZDOs collected via ZDOMan last scan.</summary>
        public static int LastScanPrefabZdos { get; private set; }

        /// <summary>Prefab-path ZDOs where FindInstance returned non-null.</summary>
        public static int LastScanPrefabLive { get; private set; }

        /// <summary>Prefab-path live views that yielded a MonsterAI.</summary>
        public static int LastScanPrefabMai { get; private set; }

        /// <summary>AnimalAI count seen during last scan (registry + scene).</summary>
        public static int LastScanAnimalAi { get; private set; }

        /// <summary>FindObjectsOfType / FindObjectsByType count.</summary>
        public static int LastScanFindObjects { get; private set; }

        /// <summary>Resources.FindObjectsOfTypeAll&lt;MonsterAI&gt; live count.</summary>
        public static int LastScanFindAll { get; private set; }

        /// <summary>Own Harmony registry snapshot size.</summary>
        public static int LastScanRegistry { get; private set; }

        /// <summary>Last scan MonsterAI count returned (deduped).</summary>
        public static int LastScanMonsterAiCount { get; private set; }

        /// <summary>
        /// MonsterAI discovery for dedicated + local. Deduped by GetInstanceID.
        /// </summary>
        public static List<MonsterAI> EnumerateMonsterAIs()
        {
            var list = new List<MonsterAI>();
            var seen = new HashSet<int>();
            var chars = 0;
            var players = 0;
            var inRange = 0;
            var playerZdos = 0;
            var instancesFound = 0;
            var baseAiInstances = 0;
            var sceneInstances = 0;
            var sceneNr = 0;
            var prefabZdos = 0;
            var prefabLive = 0;
            var prefabMai = 0;
            var animalAi = 0;
            var findObjects = 0;
            var findAll = 0;
            var registry = 0;
            var ownershipLive = 0;
            var baseAiListHits = 0;

            // (0a) EnemyOwnershipDirector live cache — 0.1.10 PRIMARY when UpdateAI runs but registry was empty
            try
            {
                var owned = FactionTactics.Dedicated.EnemyOwnershipDirector.SnapshotLiveMonsterAIs();
                ownershipLive = owned.Count;
                foreach (var ai in owned)
                {
                    if (ai == null)
                        continue;
                    if (!seen.Add(ai.GetInstanceID()))
                        continue;
                    list.Add(ai);
                    FactionTactics.HarmonyPatches.MonsterAIRegistry.Register(ai);
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("EnemyOwnership.SnapshotLive", ex);
            }

            // (0b) BaseAI.GetAllInstances / BaseAIInstances / Instances — same list MonoUpdaters uses for UpdateAI
            try
            {
                var before = list.Count;
                try
                {
                    var allBai = BaseAI.GetAllInstances();
                    if (allBai != null)
                        TryAddMonsterAIs(allBai, list, seen);
                }
                catch (Exception ex) { LogPathFailureOnce("BaseAI.GetAllInstances.early", ex); }

                try { TryAddMonsterAIs(BaseAI.BaseAIInstances, list, seen); }
                catch (Exception ex) { LogPathFailureOnce("BaseAI.BaseAIInstances.early", ex); }

                try { TryAddMonsterAIs(BaseAI.Instances, list, seen); }
                catch (Exception ex) { LogPathFailureOnce("BaseAI.Instances.early", ex); }

                baseAiListHits = list.Count - before;
                baseAiInstances = Math.Max(baseAiInstances, baseAiListHits);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("BaseAI.earlyHarvest", ex);
            }

            // (0c) Own Harmony registry — kept as backup; 0.1.10 uses instance-id dict + no OnDisable clear
            try
            {
                var reg = FactionTactics.HarmonyPatches.MonsterAIRegistry.Snapshot();
                registry = reg.Count;
                foreach (var ai in reg)
                {
                    if (ai == null)
                        continue;
                    if (!seen.Add(ai.GetInstanceID()))
                        continue;
                    list.Add(ai);
                }

                try
                {
                    animalAi = Math.Max(animalAi, FactionTactics.HarmonyPatches.MonsterAIRegistry.AnimalAiCount);
                    // Also harvest MonsterAI from any BaseAI registry entries
                    foreach (var bai in FactionTactics.HarmonyPatches.MonsterAIRegistry.SnapshotBase())
                    {
                        if (bai == null)
                            continue;
                        if (bai is MonsterAI mai && seen.Add(mai.GetInstanceID()))
                            list.Add(mai);
                    }
                }
                catch { /* ignore base harvest */ }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("MonsterAIRegistry", ex);
            }

                        // Snapshot GetAllCharacters (often empty on GPortal dedicated even with players online).
            try
            {
                var all = Character.GetAllCharacters();
                if (all != null)
                {
                    chars = all.Count;
                    foreach (var ch in all)
                    {
                        if (ch == null)
                            continue;
                        try
                        {
                            if (ch.IsPlayer())
                                players++;
                        }
                        catch { /* ignore */ }
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("GetAllCharacters", ex);
            }

            // (a) Reflect Character.s_characters
            try
            {
                AddFromCharacterSCharacters(list, seen, ref chars, ref players);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("s_characters", ex);
            }

            // Range probe around player anchors (Player.GetAllPlayers preferred)
            try
            {
                var discoveryRadius = PluginConfig.DiscoveryRadius?.Value ?? 64f;
                var radius = Mathf.Max(64f, discoveryRadius);
                var playerPositions = CollectPlayerPositions();
                if (playerPositions.Count > 0)
                    players = Math.Max(players, playerPositions.Count);

                var rangeBuf = new List<Character>();
                foreach (var pos in playerPositions)
                {
                    rangeBuf.Clear();
                    try
                    {
                        Character.GetCharactersInRange(pos, radius, rangeBuf);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var ch in rangeBuf)
                    {
                        if (ch == null)
                            continue;
                        var isPlayer = false;
                        try { isPlayer = ch.IsPlayer(); }
                        catch { /* treat as mob */ }
                        if (isPlayer)
                            continue;
                        inRange++;
                        TryAddMonsterAIFromCharacter(ch, list, seen);
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("GetCharactersInRange", ex);
            }

            // Harvest non-players from GetAllCharacters / Character.Instances
            try
            {
                var all = Character.GetAllCharacters();
                if (all != null)
                {
                    foreach (var ch in all)
                    {
                        if (ch == null)
                            continue;
                        try
                        {
                            if (ch.IsPlayer())
                                continue;
                        }
                        catch { /* keep */ }
                        TryAddMonsterAIFromCharacter(ch, list, seen);
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("GetAllCharacters-mobs", ex);
            }

            try
            {
                var instances = Character.Instances;
                if (instances != null)
                {
                    foreach (var item in instances)
                    {
                        if (item is not Character ch)
                            continue;
                        try
                        {
                            if (ch.IsPlayer())
                                continue;
                        }
                        catch { /* keep */ }
                        TryAddMonsterAIFromCharacter(ch, list, seen);
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("Character.Instances", ex);
            }

            // Player ZDO count only — NOT mob discovery
            try
            {
                if (ZNet.instance != null)
                {
                    var zdoList = ZNet.instance.GetAllCharacterZDOS();
                    if (zdoList != null)
                        playerZdos = zdoList.Count;
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("GetAllCharacterZDOS", ex);
            }

            // (b) Reflect BaseAI.m_instances + public lists
            try
            {
                baseAiInstances = AddFromBaseAiMInstances(list, seen);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("BaseAI.m_instances", ex);
            }

            try
            {
                var allBai = BaseAI.GetAllInstances();
                if (allBai != null)
                {
                    baseAiInstances = Math.Max(baseAiInstances, allBai.Count);
                    TryAddMonsterAIs(allBai, list, seen);
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("BaseAI.GetAllInstances", ex);
            }

            try { TryAddMonsterAIs(BaseAI.BaseAIInstances, list, seen); }
            catch (Exception ex) { LogPathFailureOnce("BaseAI.BaseAIInstances", ex); }

            try { TryAddMonsterAIs(BaseAI.Instances, list, seen); }
            catch (Exception ex) { LogPathFailureOnce("BaseAI.Instances", ex); }

            // (c) ZNetScene.NrOfInstances + m_instances walk
            try
            {
                if (ZNetScene.instance != null)
                {
                    try { sceneNr = ZNetScene.instance.NrOfInstances(); }
                    catch (Exception ex) { LogPathFailureOnce("ZNetScene.NrOfInstances", ex); }
                }

                sceneInstances = AddFromZNetSceneInstances(list, seen);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("ZNetScene.m_instances", ex);
            }

            // (d) ZDOMan prefab iterative + FindSectorObjects
            try
            {
                prefabZdos = AddFromZdoManEnemyPrefabs(list, seen, ref instancesFound, ref prefabLive, ref prefabMai);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("ZDOMan.prefabZdos", ex);
            }

            try
            {
                prefabZdos += AddFromZdoManSectorObjects(list, seen, ref instancesFound, ref prefabLive, ref prefabMai);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("ZDOMan.FindSectorObjects", ex);
            }

            // Scene dump once when we first see live instances (also every heartbeat via SquadDirector)
            try
            {
                if (sceneInstances > 0)
                    MaybeDumpSceneInstances(force: false);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("sceneDump", ex);
            }

            // (e) Always try FindObjectsOfType / FindObjectsByType + Resources.FindObjectsOfTypeAll
            try
            {
                findObjects = AddFromFindObjectsOfType(list, seen);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("FindObjectsOfType", ex);
            }

            try
            {
                findAll = AddFromResourcesFindObjectsOfTypeAll(list, seen);
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("Resources.FindObjectsOfTypeAll", ex);
            }

            LastScanCharacterCount = chars;
            LastScanPlayerCount = players;
            LastScanInRangeCount = inRange;
            LastScanPlayerZdoCount = playerZdos;
            LastScanInstancesFound = instancesFound;
            LastScanBaseAiInstances = baseAiInstances;
            LastScanSceneInstances = sceneInstances;
            LastScanSceneNrOfInstances = sceneNr;
            LastScanPrefabZdos = prefabZdos;
            LastScanPrefabLive = prefabLive;
            LastScanPrefabMai = prefabMai;
            LastScanAnimalAi = animalAi;
            LastScanFindObjects = findObjects;
            LastScanFindAll = findAll;
            LastScanRegistry = registry;
            LastScanMonsterAiCount = list.Count;
            return list;
        }

        private static void AddFromCharacterSCharacters(
            List<MonsterAI> list, HashSet<int> seen, ref int chars, ref int players)
        {
            if (!_sCharactersResolved)
            {
                _sCharactersResolved = true;
                try
                {
                    _sCharactersField = typeof(Character).GetField(
                        "s_characters",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
                catch (Exception ex)
                {
                    LogPathFailureOnce("s_characters.resolve", ex);
                    _sCharactersField = null;
                }
            }

            if (_sCharactersField == null)
                return;

            object? raw;
            try { raw = _sCharactersField.GetValue(null); }
            catch (Exception ex)
            {
                LogPathFailureOnce("s_characters.get", ex);
                return;
            }

            if (raw is not IList ilist)
                return;

            chars = Math.Max(chars, ilist.Count);
            var localPlayers = 0;
            foreach (var item in ilist)
            {
                if (item is not Character ch || ch == null)
                    continue;
                try
                {
                    if (ch.IsPlayer())
                    {
                        localPlayers++;
                        continue;
                    }
                }
                catch { /* treat as mob */ }

                TryAddMonsterAIFromCharacter(ch, list, seen);
            }

            players = Math.Max(players, localPlayers);
        }

        private static int AddFromBaseAiMInstances(List<MonsterAI> list, HashSet<int> seen)
        {
            if (!_baseAiInstancesResolved)
            {
                _baseAiInstancesResolved = true;
                try
                {
                    _baseAiInstancesField = typeof(BaseAI).GetField(
                        "m_instances",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
                catch (Exception ex)
                {
                    LogPathFailureOnce("BaseAI.m_instances.resolve", ex);
                    _baseAiInstancesField = null;
                }
            }

            if (_baseAiInstancesField == null)
                return 0;

            object? raw;
            try { raw = _baseAiInstancesField.GetValue(null); }
            catch (Exception ex)
            {
                LogPathFailureOnce("BaseAI.m_instances.get", ex);
                return 0;
            }

            if (raw == null)
                return 0;

            var count = raw is ICollection coll ? coll.Count : 0;
            TryAddMonsterAIs(raw as IEnumerable, list, seen);
            return count;
        }

        private static int AddFromZNetSceneInstances(List<MonsterAI> list, HashSet<int> seen)
        {
            if (ZNetScene.instance == null)
                return 0;

            if (!_zNetSceneInstancesFieldResolved)
            {
                _zNetSceneInstancesFieldResolved = true;
                try
                {
                    _zNetSceneInstancesField = typeof(ZNetScene).GetField(
                        "m_instances",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                catch (Exception ex)
                {
                    LogPathFailureOnce("ZNetScene.m_instances.resolve", ex);
                    _zNetSceneInstancesField = null;
                }
            }

            if (_zNetSceneInstancesField == null)
                return 0;

            object? raw;
            try { raw = _zNetSceneInstancesField.GetValue(ZNetScene.instance); }
            catch (Exception ex)
            {
                LogPathFailureOnce("ZNetScene.m_instances.get", ex);
                return 0;
            }

            if (raw == null)
                return 0;

            var count = raw is ICollection coll ? coll.Count : 0;

            if (raw is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (entry.Value is ZNetView nv && nv != null)
                        TryAddFromZNetView(nv, list, seen);
                }
                return count;
            }

            if (raw is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    ZNetView? nv = null;
                    if (item is ZNetView direct)
                        nv = direct;
                    else if (item != null)
                    {
                        var vt = item.GetType();
                        if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                            nv = vt.GetProperty("Value")?.GetValue(item) as ZNetView;
                    }

                    if (nv != null)
                        TryAddFromZNetView(nv, list, seen);
                }
            }

            return count;
        }

        private static void TryAddFromZNetView(ZNetView nv, List<MonsterAI> list, HashSet<int> seen, ref int maiHits)
        {
            try
            {
                // Register any BaseAI on the view into Harmony registry (MonsterAI subclass preferred).
                FactionTactics.HarmonyPatches.MonsterAIRegistry.RegisterFromComponent(nv);

                MonsterAI? mai = null;
                try { mai = nv.GetComponent<MonsterAI>(); } catch { /* ignore */ }
                if (mai == null)
                {
                    try { mai = nv.GetComponentInChildren<MonsterAI>(true); } catch { /* ignore */ }
                }
                if (mai != null)
                {
                    maiHits++;
                    if (seen.Add(mai.GetInstanceID()))
                        list.Add(mai);
                    return;
                }

                BaseAI? bai = null;
                try { bai = nv.GetComponent<BaseAI>(); } catch { /* ignore */ }
                if (bai == null)
                {
                    try { bai = nv.GetComponentInChildren<BaseAI>(true); } catch { /* ignore */ }
                }
                if (bai != null)
                {
                    FactionTactics.HarmonyPatches.MonsterAIRegistry.RegisterBase(bai);
                    if (bai is MonsterAI asMai)
                    {
                        maiHits++;
                        if (seen.Add(asMai.GetInstanceID()))
                            list.Add(asMai);
                        return;
                    }
                }

                Character? ch = null;
                try { ch = nv.GetComponent<Character>(); } catch { /* ignore */ }
                if (ch == null)
                {
                    try { ch = nv.GetComponentInChildren<Character>(true); } catch { /* ignore */ }
                }
                if (ch == null)
                    return;
                try
                {
                    if (ch.IsPlayer())
                        return;
                }
                catch { /* keep */ }

                var before = list.Count;
                TryAddMonsterAIFromCharacter(ch, list, seen);
                if (list.Count > before)
                    maiHits++;
            }
            catch { /* per-view */ }
        }

        private static void TryAddFromZNetView(ZNetView nv, List<MonsterAI> list, HashSet<int> seen)
        {
            var maiHits = 0;
            TryAddFromZNetView(nv, list, seen, ref maiHits);
        }

        private static int AddFromZdoManEnemyPrefabs(
            List<MonsterAI> list, HashSet<int> seen, ref int instancesFound,
            ref int prefabLive, ref int prefabMai)
        {
            if (ZDOMan.instance == null || ZNetScene.instance == null)
                return 0;

            var zdoBuf = new List<ZDO>();
            var total = 0;
            foreach (var prefab in KnownEnemyPrefabs)
            {
                zdoBuf.Clear();
                var index = 0;
                try
                {
                    // true = finished; false = more batches remain
                    while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, zdoBuf, ref index))
                    {
                    }
                }
                catch (Exception ex)
                {
                    LogPathFailureOnce("GetAllZDOsWithPrefabIterative:" + prefab, ex);
                    continue;
                }

                total += zdoBuf.Count;
                foreach (var zdo in zdoBuf)
                    TryResolveZdoToMonsterAI(zdo, list, seen, ref instancesFound, ref prefabLive, ref prefabMai);
            }

            return total;
        }

        private static int AddFromZdoManSectorObjects(
            List<MonsterAI> list, HashSet<int> seen, ref int instancesFound,
            ref int prefabLive, ref int prefabMai)
        {
            if (ZDOMan.instance == null || ZNetScene.instance == null)
                return 0;

            var playerPositions = CollectPlayerPositions();
            if (playerPositions.Count == 0)
                return 0;

            var sectorObjects = new List<ZDO>();
            var distantObjects = new List<ZDO>();
            var visited = new HashSet<long>();
            var hits = 0;
            var simDist = SimulationDistance.OriginalDistance;

            foreach (var pos in playerPositions)
            {
                Vector2s zone;
                try { zone = ZoneSystem.GetZone(pos); }
                catch (Exception ex)
                {
                    LogPathFailureOnce("ZoneSystem.GetZone", ex);
                    continue;
                }

                sectorObjects.Clear();
                distantObjects.Clear();
                try
                {
                    ZDOMan.instance.FindSectorObjects(zone, simDist, sectorObjects, distantObjects);
                }
                catch (Exception ex)
                {
                    LogPathFailureOnce("FindSectorObjects", ex);
                    continue;
                }

                foreach (var batch in new[] { sectorObjects, distantObjects })
                {
                    foreach (var zdo in batch)
                    {
                        if (zdo == null)
                            continue;
                        long key;
                        try { key = ValheimIds.ToLong(zdo.m_uid); }
                        catch { key = zdo.GetHashCode(); }
                        if (!visited.Add(key))
                            continue;
                        if (!IsLikelyEnemyPrefabZdo(zdo))
                            continue;
                        hits++;
                        TryResolveZdoToMonsterAI(zdo, list, seen, ref instancesFound, ref prefabLive, ref prefabMai);
                    }
                }
            }

            return hits;
        }

        public static bool IsLikelyEnemyPrefabZdo(ZDO zdo)
        {
            try
            {
                var hash = zdo.GetPrefab();
                if (hash == 0)
                    return false;
                var go = ZNetScene.instance.GetPrefab(hash);
                if (go == null)
                    return false;
                var name = go.name ?? "";
                foreach (var prefab in KnownEnemyPrefabs)
                {
                    if (name.StartsWith(prefab, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return name.StartsWith("Skeleton", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Greydwarf", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Draugr", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Goblin", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Fuling", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Charred", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Wolf", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Blob", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Seeker", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Tick", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Gjall", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Asksvin", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Drake", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Hatchling", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Troll", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TryResolveZdoToMonsterAI(
            ZDO zdo, List<MonsterAI> list, HashSet<int> seen, ref int instancesFound,
            ref int prefabLive, ref int prefabMai)
        {
            if (zdo == null || ZNetScene.instance == null)
                return;

            try
            {
                var nv = ZNetScene.instance.FindInstance(zdo);
                if (nv != null)
                {
                    instancesFound++;
                    prefabLive++;
                    var maiHits = 0;
                    TryAddFromZNetView(nv, list, seen, ref maiHits);
                    prefabMai += maiHits;
                    return;
                }
            }
            catch { /* try uid */ }

            try
            {
                var go = ZNetScene.instance.FindInstance(zdo.m_uid);
                if (go == null)
                    return;
                instancesFound++;
                prefabLive++;
                FactionTactics.HarmonyPatches.MonsterAIRegistry.RegisterFromComponent(go.GetComponent<ZNetView>());

                MonsterAI? mai = null;
                try { mai = go.GetComponent<MonsterAI>(); } catch { /* ignore */ }
                if (mai == null)
                {
                    try { mai = go.GetComponentInChildren<MonsterAI>(true); } catch { /* ignore */ }
                }
                if (mai != null)
                {
                    prefabMai++;
                    if (seen.Add(mai.GetInstanceID()))
                        list.Add(mai);
                    return;
                }

                BaseAI? bai = null;
                try { bai = go.GetComponent<BaseAI>(); } catch { /* ignore */ }
                if (bai == null)
                {
                    try { bai = go.GetComponentInChildren<BaseAI>(true); } catch { /* ignore */ }
                }
                if (bai != null)
                    FactionTactics.HarmonyPatches.MonsterAIRegistry.RegisterBase(bai);

                Character? ch = null;
                try { ch = go.GetComponent<Character>(); } catch { /* ignore */ }
                if (ch == null)
                {
                    try { ch = go.GetComponentInChildren<Character>(true); } catch { /* ignore */ }
                }
                if (ch == null)
                    return;
                try
                {
                    if (ch.IsPlayer())
                        return;
                }
                catch { /* keep */ }

                var before = list.Count;
                TryAddMonsterAIFromCharacter(ch, list, seen);
                if (list.Count > before)
                    prefabMai++;
            }
            catch { /* ignore */ }
        }

        private static int AddFromFindObjectsOfType(List<MonsterAI> list, HashSet<int> seen)
        {
            MonsterAI[]? found = null;
            try
            {
                found = UnityEngine.Object.FindObjectsByType<MonsterAI>(FindObjectsSortMode.None);
            }
            catch
            {
                try { found = UnityEngine.Object.FindObjectsOfType<MonsterAI>(); }
                catch (Exception ex)
                {
                    LogPathFailureOnce("FindObjectsOfType.inner", ex);
                    return 0;
                }
            }

            if (found == null)
                return 0;

            foreach (var ai in found)
            {
                if (ai == null)
                    continue;
                if (!seen.Add(ai.GetInstanceID()))
                    continue;
                list.Add(ai);
            }

            return found.Length;
        }

        private static int AddFromResourcesFindObjectsOfTypeAll(List<MonsterAI> list, HashSet<int> seen)
        {
            MonsterAI[]? found;
            try { found = Resources.FindObjectsOfTypeAll<MonsterAI>(); }
            catch (Exception ex)
            {
                LogPathFailureOnce("Resources.FindObjectsOfTypeAll.inner", ex);
                return 0;
            }

            if (found == null)
                return 0;

            var live = 0;
            foreach (var ai in found)
            {
                if (ai == null)
                    continue;
                try
                {
                    if (!ai.gameObject.scene.IsValid())
                        continue;
                }
                catch { /* keep */ }

                live++;
                if (!seen.Add(ai.GetInstanceID()))
                    continue;
                list.Add(ai);
            }

            return live;
        }

        private static void TryAddMonsterAIFromCharacter(Character ch, List<MonsterAI> list, HashSet<int> seen)
        {
            if (ch == null)
                return;

            MonsterAI? mai = null;
            try { mai = ch.GetComponent<MonsterAI>(); }
            catch { /* ignore */ }

            if (mai == null)
            {
                try { mai = ch.GetComponentInChildren<MonsterAI>(true); }
                catch { /* ignore */ }
            }

            if (mai == null)
            {
                try
                {
                    var asBase = ch.GetComponent<BaseAI>() as MonsterAI
                                 ?? ch.GetComponentInChildren<BaseAI>(true) as MonsterAI;
                    if (asBase != null)
                        mai = asBase;
                }
                catch { /* ignore */ }
            }

            if (mai == null)
            {
                try
                {
                    var bai = ch.GetBaseAI();
                    if (bai is MonsterAI fromGet)
                        mai = fromGet;
                    else if (bai != null)
                    {
                        FactionTactics.HarmonyPatches.MonsterAIRegistry.RegisterBase(bai);
                        mai = bai.GetComponent<MonsterAI>()
                              ?? bai.GetComponentInChildren<MonsterAI>(true);
                    }
                }
                catch { /* no MonsterAI */ }
            }

            if (mai == null)
                return;
            FactionTactics.HarmonyPatches.MonsterAIRegistry.Register(mai);
            if (!seen.Add(mai.GetInstanceID()))
                return;
            list.Add(mai);
        }

        private static bool TryAddMonsterAIs(IEnumerable? source, List<MonsterAI> list, HashSet<int> seen)
        {
            if (source == null)
                return false;
            foreach (var item in source)
            {
                if (item == null)
                    continue;
                if (item is MonsterAI mai)
                {
                    if (seen.Add(mai.GetInstanceID()))
                    {
                        list.Add(mai);
                        FactionTactics.HarmonyPatches.MonsterAIRegistry.Register(mai);
                    }
                    continue;
                }

                if (item is BaseAI bai)
                {
                    try
                    {
                        var asMai = bai as MonsterAI ?? bai.GetComponent<MonsterAI>();
                        if (asMai != null && seen.Add(asMai.GetInstanceID()))
                        {
                            list.Add(asMai);
                            FactionTactics.HarmonyPatches.MonsterAIRegistry.Register(asMai);
                        }
                    }
                    catch { /* ignore */ }
                    continue;
                }

                // MonoUpdaters iterates List<IUpdateAI> — cast via Component
                if (item is Component comp)
                {
                    try
                    {
                        var asMai = comp as MonsterAI ?? comp.GetComponent<MonsterAI>();
                        if (asMai != null && seen.Add(asMai.GetInstanceID()))
                        {
                            list.Add(asMai);
                            FactionTactics.HarmonyPatches.MonsterAIRegistry.Register(asMai);
                        }
                    }
                    catch { /* ignore */ }
                }
            }

            return true;
        }

        private static void LogPathFailureOnce(string path, Exception ex)
        {
            if (!_loggedPathFailures.Add(path))
                return;
            Plugin.Log?.LogWarning(
                $"discovery path {path} failed: {ex.GetType().Name}: {ex.Message}");
        }

        /// <summary>
        /// Player world anchors. Prefer <see cref="Player.GetAllPlayers"/> (dedicated-safe when
        /// s_characters is empty), then Character players, then ZNet peer positions.
        /// </summary>
        public static List<Vector3> CollectPlayerPositions()
        {
            var positions = new List<Vector3>();

            try
            {
                var all = Player.GetAllPlayers();
                if (all != null)
                {
                    foreach (var p in all)
                    {
                        if (p == null)
                            continue;
                        try
                        {
                            if (p.IsDead())
                                continue;
                        }
                        catch { /* keep */ }

                        positions.Add(p.transform.position);
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("Player.GetAllPlayers.positions", ex);
            }

            if (positions.Count > 0)
                return positions;

            try
            {
                foreach (var ch in EnumeratePlayerCharacters())
                {
                    if (ch == null)
                        continue;
                    try
                    {
                        if (ch.IsDead())
                            continue;
                    }
                    catch { /* keep */ }

                    positions.Add(ch.transform.position);
                }
            }
            catch { /* fall through */ }

            if (positions.Count > 0)
                return positions;

            try
            {
                if (ZNet.instance != null)
                {
                    var peers = ZNet.instance.GetPlayerList();
                    if (peers != null)
                    {
                        foreach (var info in peers)
                        {
                            var pos = info.m_position;
                            if (pos == Vector3.zero && !info.m_publicPosition)
                                continue;
                            positions.Add(pos);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("ZNet.GetPlayerList", ex);
            }

            return positions;
        }

        /// <summary>Live <see cref="Player"/> components. Prefer GetAllPlayers on dedicated.</summary>
        public static List<Player> CollectPlayers()
        {
            var list = new List<Player>();
            try
            {
                var all = Player.GetAllPlayers();
                if (all != null)
                {
                    foreach (var p in all)
                    {
                        if (p != null)
                            list.Add(p);
                    }
                }

                if (list.Count > 0)
                    return list;
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("Player.GetAllPlayers", ex);
            }

            try
            {
                foreach (var ch in EnumeratePlayerCharacters())
                {
                    if (ch is Player p)
                        list.Add(p);
                }
            }
            catch { /* ignore */ }

            return list;
        }

        /// <summary>Characters where <see cref="Character.IsPlayer"/> is true.</summary>
        public static List<Character> EnumeratePlayerCharacters()
        {
            var list = new List<Character>();
            try
            {
                var all = Character.GetAllCharacters();
                if (all != null)
                {
                    foreach (var ch in all)
                    {
                        if (ch == null)
                            continue;
                        try
                        {
                            if (ch.IsPlayer())
                                list.Add(ch);
                        }
                        catch { /* skip */ }
                    }

                    if (list.Count > 0)
                        return list;
                }
            }
            catch { /* fall through */ }

            try
            {
                var instances = Character.Instances;
                if (instances != null)
                {
                    foreach (var item in instances)
                    {
                        if (item is Character ch)
                        {
                            try
                            {
                                if (ch.IsPlayer())
                                    list.Add(ch);
                            }
                            catch { /* skip */ }
                        }
                    }

                    if (list.Count > 0)
                        return list;
                }
            }
            catch { /* fall through */ }

            try
            {
                var all = Player.GetAllPlayers();
                if (all != null)
                {
                    foreach (var p in all)
                    {
                        if (p != null)
                            list.Add(p);
                    }
                }
            }
            catch { /* dedicated empty */ }

            return list;
        }


        /// <summary>
        /// Dump up to 25 ZNetScene.m_instances entries: prefab name, FindInstance live, component types.
        /// Called from heartbeat (15s) or first time sceneInstances&gt;0.
        /// </summary>
        public static void MaybeDumpSceneInstances(bool force)
        {
            if (ZNetScene.instance == null)
                return;

            var now = Time.realtimeSinceStartup;
            if (!force && _sceneDumpDone && (now - _lastSceneDumpRealtime) < 14f)
                return;

            if (!_zNetSceneInstancesFieldResolved)
            {
                _zNetSceneInstancesFieldResolved = true;
                try
                {
                    _zNetSceneInstancesField = typeof(ZNetScene).GetField(
                        "m_instances",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                catch
                {
                    _zNetSceneInstancesField = null;
                }
            }

            if (_zNetSceneInstancesField == null)
                return;

            object? raw;
            try { raw = _zNetSceneInstancesField.GetValue(ZNetScene.instance); }
            catch { return; }
            if (raw == null)
                return;

            var entries = new List<ZNetView>();
            try
            {
                if (raw is IDictionary dict)
                {
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (entry.Value is ZNetView nv && nv != null)
                            entries.Add(nv);
                    }
                }
                else if (raw is IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        ZNetView? nv = null;
                        if (item is ZNetView direct)
                            nv = direct;
                        else if (item != null)
                        {
                            var vt = item.GetType();
                            if (vt.IsGenericType && vt.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                                nv = vt.GetProperty("Value")?.GetValue(item) as ZNetView;
                        }
                        if (nv != null)
                            entries.Add(nv);
                    }
                }
            }
            catch (Exception ex)
            {
                LogPathFailureOnce("sceneDump.enumerate", ex);
                return;
            }

            if (entries.Count == 0 && !force)
                return;

            var limit = Math.Min(25, entries.Count);
            for (var i = 0; i < limit; i++)
            {
                var nv = entries[i];
                try
                {
                    var prefabName = "?";
                    var live = false;
                    try
                    {
                        var zdo = nv.GetZDO();
                        if (zdo != null)
                        {
                            live = ZNetScene.instance.FindInstance(zdo) != null;
                            var hash = zdo.GetPrefab();
                            var prefabGo = hash != 0 ? ZNetScene.instance.GetPrefab(hash) : null;
                            if (prefabGo != null)
                                prefabName = prefabGo.name;
                            else if (nv.gameObject != null)
                                prefabName = nv.gameObject.name;
                        }
                        else if (nv.gameObject != null)
                            prefabName = nv.gameObject.name;
                    }
                    catch
                    {
                        try { prefabName = nv.gameObject != null ? nv.gameObject.name : "?"; }
                        catch { prefabName = "?"; }
                    }

                    var comps = DescribeAiComponents(nv.gameObject);
                    Plugin.Log?.LogInfo(
                        $"FactionTactics sceneDump: [{i}] prefab={prefabName} live={live} comps=[{comps}]");
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogInfo($"FactionTactics sceneDump: [{i}] error={ex.GetType().Name}:{ex.Message}");
                }
            }

            _sceneDumpDone = true;
            _lastSceneDumpRealtime = now;
            Plugin.Log?.LogInfo(
                $"FactionTactics sceneDump: done showing={limit}/{entries.Count}");
        }

        private static string DescribeAiComponents(GameObject? go)
        {
            if (go == null)
                return "null";
            var names = new List<string>();
            try
            {
                void AddIf(string label, bool present)
                {
                    if (present)
                        names.Add(label);
                }

                AddIf("Character", go.GetComponent<Character>() != null || go.GetComponentInChildren<Character>(true) != null);
                AddIf("Humanoid", go.GetComponent<Humanoid>() != null || go.GetComponentInChildren<Humanoid>(true) != null);
                AddIf("BaseAI", go.GetComponent<BaseAI>() != null || go.GetComponentInChildren<BaseAI>(true) != null);
                AddIf("MonsterAI", go.GetComponent<MonsterAI>() != null || go.GetComponentInChildren<MonsterAI>(true) != null);
                AddIf("AnimalAI", go.GetComponent<AnimalAI>() != null || go.GetComponentInChildren<AnimalAI>(true) != null);
                AddIf("ZNetView", go.GetComponent<ZNetView>() != null);

                try
                {
                    var bai = go.GetComponent<BaseAI>() ?? go.GetComponentInChildren<BaseAI>(true);
                    if (bai != null)
                    {
                        var tn = bai.GetType().Name;
                        if (!names.Contains(tn))
                            names.Add(tn);
                    }
                }
                catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                return "err:" + ex.GetType().Name;
            }

            return names.Count > 0 ? string.Join(",", names) : "none";
        }

        /// <summary>One-shot diagnostic for dedicated discovery paths.</summary>
        public static void LogDedicatedDiscoveryOnce(int players, int monsterAi)
        {
            if (_loggedDedicatedDiscovery)
                return;
            _loggedDedicatedDiscovery = true;
            var ownership = 0;
            var regDiag = "?";
            try { ownership = FactionTactics.Dedicated.EnemyOwnershipDirector.SnapshotLiveMonsterAIs().Count; } catch { /* ignore */ }
            try { regDiag = FactionTactics.HarmonyPatches.MonsterAIRegistry.Diagnostics(); } catch { /* ignore */ }
            Plugin.Log?.LogInfo(
                $"dedicated discovery 0.1.10: chars={LastScanCharacterCount} " +
                $"baseAiInstances={LastScanBaseAiInstances} " +
                $"sceneInstances={LastScanSceneInstances} sceneNr={LastScanSceneNrOfInstances} " +
                $"prefabZdos={LastScanPrefabZdos} prefabLive={LastScanPrefabLive} prefabMai={LastScanPrefabMai} " +
                $"findObjects={LastScanFindObjects} findAll={LastScanFindAll} registry={LastScanRegistry} animalAI={LastScanAnimalAi} " +
                $"ownershipLive={ownership} regDiag=[{regDiag}] " +
                $"playerZdos={LastScanPlayerZdoCount} players={players} monsterAI={monsterAi}");
        }

    }
}
#endif
