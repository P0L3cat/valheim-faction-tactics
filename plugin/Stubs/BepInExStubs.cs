#if !VALHEIM_REFS
// Minimal stand-ins so the greenfield solution compiles without Valheim/BepInEx DLLs.
// When ValheimDir is set and assemblies exist, VALHEIM_REFS is defined and these are excluded.

using System;
using System.Collections.Generic;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public BepInPlugin(string GUID, string Name, string Version) { }
    }

    public class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        protected Configuration.ConfigFile Config { get; } = new Configuration.ConfigFile();
        protected Logging.ManualLogSource Logger { get; } = new Logging.ManualLogSource("FactionTactics");
    }

    namespace Logging
    {
        public class ManualLogSource
        {
            private readonly string _name;
            public ManualLogSource(string name) => _name = name;
            public void LogInfo(object data) { }
            public void LogWarning(object data) { }
            public void LogError(object data) { }
            public void LogDebug(object data) { }
        }
    }

    namespace Configuration
    {
        public class ConfigFile
        {
            public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
                => new ConfigEntry<T>(defaultValue);
        }

        public class ConfigEntry<T>
        {
            public T Value { get; set; }
            public ConfigEntry(T value) => Value = value;
        }
    }
}

namespace HarmonyLib
{
    public class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll() { }
        public void PatchAll(System.Reflection.Assembly assembly) { }
        public void UnpatchSelf() { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] args) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyTranspiler : Attribute { }
}

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = "";
        public static T FindObjectOfType<T>() where T : Object => null!;
        public static T[] FindObjectsOfType<T>() where T : Object => Array.Empty<T>();
    }

    public class MonoBehaviour : Object
    {
        // Unity message methods are discovered by name; do not declare them here
        // (avoids CS0114 hide warnings on Plugin.Awake/Update/OnDestroy).
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static float Distance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x; var dy = a.y - b.y; var dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
    }

    public static class Time
    {
        public static float deltaTime => 0.016f;
        public static float time => 0f;
    }

    public class Component : Object
    {
        public Transform transform { get; } = new Transform();
        public T GetComponent<T>() => default!;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
    }

    public class GameObject : Object
    {
        public Transform transform { get; } = new Transform();
        public T GetComponent<T>() => default!;
    }
}

namespace FactionTactics.Stubs
{
    /// <summary>Stand-in for Valheim Character when compiling without game DLLs.</summary>
    public class Character : UnityEngine.Component
    {
        public string m_name = "";
        public bool IsDead() => false;
        public UnityEngine.Vector3 GetCenterPoint() => transform.position;
        public long GetZDOID() => GetHashCode();
    }

    /// <summary>Stand-in for MonsterAI.</summary>
    public class MonsterAI : UnityEngine.MonoBehaviour
    {
        public Character? m_character;
        public Character? GetTarget() => null;
        public void SetTarget(Character? c) { }
        public void MoveTo(float dt, UnityEngine.Vector3 point, float dist, bool run) { }
        public void StopMoving() { }
        public bool IsAlerted() => false;
    }

    /// <summary>Stand-in for Humanoid (weapon heuristics).</summary>
    public class Humanoid : Character
    {
        public ItemDrop.ItemData? GetCurrentWeapon() => null;
    }

    public static class ItemDrop
    {
        public class ItemData
        {
            public SharedData m_shared = new SharedData();
            public class SharedData
            {
                public string m_name = "";
                public bool m_attack => false;
                public SkillType m_skillType = SkillType.None;
            }
        }
    }

    public enum SkillType
    {
        None,
        Bows,
        Spears,
        Swords,
        Clubs,
        Polearms,
    }

    /// <summary>Stand-in for ZNet server check.</summary>
    public class ZNet
    {
        public static ZNet? instance { get; set; }
        public bool IsServer() => true;
    }
}
#endif
