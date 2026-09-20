#if VALHEIM_REFS
using FactionTactics.Orders;
using HarmonyLib;

namespace FactionTactics.HarmonyPatches
{
    /// <summary>1.0.3: register FT_MemberIntents routed RPC when ZNet wakes (server + listen).</summary>
    [HarmonyPatch(typeof(ZNet), "Awake")]
    public static class ZNet_Awake_IntentRpcRegistration_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            IntentRpcSync.EnsureRegistered();
        }
    }
}
#endif
