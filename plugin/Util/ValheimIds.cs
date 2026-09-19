namespace FactionTactics.Util
{
    /// <summary>
    /// Stable long keys for Character / ZDO identity used by discovery + OrderApplicator intents.
    /// </summary>
    public static class ValheimIds
    {
#if VALHEIM_REFS
        /// <summary>
        /// Packs a Valheim <see cref="ZDOID"/> into a <see cref="long"/> dictionary key.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Verified against <c>assembly_valheim.dll</c> (Mono.Cecil): ZDOID exposes
        /// <c>UserID</c> (long), <c>UserKey</c> (ushort), <c>ID</c> (uint).
        /// <c>Character.GetZDOID()</c> returns <c>ZDOID</c> (public).
        /// </para>
        /// <para>
        /// <b>Packing:</b> <c>((UserID &amp; 0xFFFFFFFFL) &lt;&lt; 32) | ID</c>
        /// — low 32 bits of peer <c>UserID</c> in the high half, ZDO <c>ID</c> in the low half.
        /// This is the common Valheim-mod layout (userID &lt;&lt; 32 | id). Peer ids that differ
        /// only in bits above 32 would collide; that does not occur for typical dedicated /
        /// local UserID values. Prefer this over <c>GetHashCode()</c> so intents survive
        /// across frames.
        /// </para>
        /// </remarks>
        public static long ToLong(ZDOID zdoid)
        {
            unchecked
            {
                return ((zdoid.UserID & 0xFFFFFFFFL) << 32) | zdoid.ID;
            }
        }

        /// <summary>Stable id for a live Character, or 0 if null.</summary>
        public static long FromCharacter(Character? ch)
        {
            if (ch == null)
                return 0;
            return ToLong(ch.GetZDOID());
        }

        /// <summary>
        /// <c>BaseAI.m_character</c> is protected (Family) — read via HarmonyX Traverse
        /// (same assembly cannot access Family fields). Falls back to GetComponent.
        /// </summary>
        public static Character? GetCharacter(BaseAI? ai)
        {
            if (ai == null)
                return null;
            var ch = HarmonyLib.Traverse.Create(ai).Field("m_character").GetValue<Character>();
            if (ch != null)
                return ch;
            return ai.GetComponent<Character>();
        }
#else
        /// <summary>Stub-mode: Character.GetZDOID already returns a long stand-in.</summary>
        public static long ToLong(long stubId) => stubId;
#endif
    }
}
