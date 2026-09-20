#if VALHEIM_REFS
using FactionTactics.Orders;
using FactionTactics.ConsoleCmds;
using HarmonyLib;

namespace FactionTactics.HarmonyPatches
{
    /// <summary>
    /// 1.0.4: register FT_MemberIntents on ZNet.Awake, OnNewConnection, and once-until-registered
    /// via Game.Update / ZNet.Update so the handler exists after join (not only at Awake).
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static class ZNet_Awake_IntentRpcRegistration_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IntentRpcSync.EnsureRegistered();
            FtConfigRpc.EnsureRegistered();
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    public static class ZNet_OnNewConnection_IntentRpcRegistration_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(/* ZNetPeer peer */)
        {
            IntentRpcSync.EnsureRegistered();
            FtConfigRpc.EnsureRegistered();
        }
    }

    [HarmonyPatch(typeof(ZNet), "Update")]
    public static class ZNet_Update_IntentRpcRegistration_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!IntentRpcSync.IsRegistered)
                IntentRpcSync.EnsureRegistered();
            FtConfigRpc.EnsureRegistered();
        }
    }

    [HarmonyPatch(typeof(Game), "Update")]
    public static class Game_Update_IntentRpcRegistration_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!IntentRpcSync.IsRegistered)
                IntentRpcSync.EnsureRegistered();
            FtConfigRpc.EnsureRegistered();
        }
    }
}
#endif
