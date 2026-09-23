namespace FactionTactics.Orders
{
    /// <summary>
    /// Product + intent-schema versions. Client and server must match SchemaVersion
    /// or executors refuse intents loudly. Keep ProductVersion in sync across both DLLs.
    /// </summary>
    public static class FtVersion
    {
        public const string ProductVersion = "1.0.10";
        public const int IntentSchemaVersion = IntentZdoCodec.SchemaVersion;
    }
}
