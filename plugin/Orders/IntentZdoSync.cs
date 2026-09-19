using System;
using FactionTactics.Doctrine;
using FactionTactics.Util;
using UnityEngine;

namespace FactionTactics.Orders
{
    /// <summary>
    /// Replicates <see cref="MemberIntent"/> through ZDO custom fields (schema v1).
    /// Server commander writes; owning client executor reads.
    /// </summary>
    public static class IntentZdoSync
    {
        public const string KeyVersion = "ft_iv";
        public const string KeyFlags = "ft_flg";
        public const string KeyOrder = "ft_ord";
        public const string KeyFormation = "ft_frm";
        public const string KeyStance = "ft_stn";
        public const string KeyRole = "ft_rol";
        public const string KeySlot = "ft_slot";
        public const string KeyFocus = "ft_focus";
        public const string KeyTime = "ft_t";
        public const string KeySquadHash = "ft_sq";

        public static long Writes { get; private set; }
        public static long ReadsOk { get; private set; }
        public static long ReadsMiss { get; private set; }
        public static long ReadsStale { get; private set; }
        public static long SchemaMismatches { get; private set; }
        private static bool _loggedSchemaMismatch;

        public static void ResetCounters()
        {
            Writes = ReadsOk = ReadsMiss = ReadsStale = SchemaMismatches = 0;
            _loggedSchemaMismatch = false;
        }

        public static int StableHash(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;
            unchecked
            {
                int hash = 5381;
                foreach (var c in s!)
                    hash = ((hash << 5) + hash) ^ c;
                return hash;
            }
        }

#if VALHEIM_REFS
        public static bool TryGetZdo(MonsterAI? ai, out ZDO zdo)
        {
            zdo = null!;
            if (ai == null)
                return false;
            try
            {
                var nv = ai.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid())
                    return false;
                var z = nv.GetZDO();
                if (z == null)
                    return false;
                zdo = z;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsNetOwner(MonsterAI? ai)
        {
            if (ai == null)
                return false;
            try
            {
                var nv = ai.GetComponent<ZNetView>();
                return nv != null && nv.IsValid() && nv.IsOwner();
            }
            catch
            {
                return false;
            }
        }

        public static bool Write(ZDO zdo, MemberIntent intent, float nowSeconds)
        {
            if (zdo == null || intent == null)
                return false;
            try
            {
                zdo.Set(KeyVersion, IntentZdoCodec.SchemaVersion);
                zdo.Set(KeyFlags, (int)IntentZdoCodec.PackFlags(intent));
                zdo.Set(KeyOrder, (int)intent.OrderKind);
                zdo.Set(KeyFormation, (int)intent.Formation);
                zdo.Set(KeyStance, (int)intent.Stance);
                zdo.Set(KeyRole, (int)intent.Role);
                zdo.Set(KeySlot, intent.DesiredPosition);
                zdo.Set(KeyFocus, intent.FocusTargetId ?? 0L);
                zdo.Set(KeyTime, nowSeconds);
                zdo.Set(KeySquadHash, StableHash(intent.SquadId));
                Writes++;
                return true;
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"IntentZdoSync.Write failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        public static bool WriteFromMonsterAI(MonsterAI ai, MemberIntent intent, float nowSeconds)
            => TryGetZdo(ai, out var zdo) && Write(zdo, intent, nowSeconds);

        public static bool TryRead(ZDO zdo, float nowSeconds, out MemberIntent intent)
        {
            intent = null!;
            if (zdo == null)
            {
                ReadsMiss++;
                return false;
            }

            try
            {
                var ver = zdo.GetInt(KeyVersion, 0);
                if (ver != IntentZdoCodec.SchemaVersion)
                {
                    SchemaMismatches++;
                    ReadsMiss++;
                    if (!_loggedSchemaMismatch)
                    {
                        _loggedSchemaMismatch = true;
                        FactionTacticsLog.Debug(
                            $"[FT] INTENT SCHEMA MISMATCH: ZDO ft_iv={ver} local={IntentZdoCodec.SchemaVersion} product={FtVersion.ProductVersion}. " +
                            "Update FactionTactics server+client packs to the same 0.3.x release.");
                        try
                        {
                            UnityEngine.Debug.LogError(
                                $"[FactionTactics] Intent schema mismatch (zdo={ver} local={IntentZdoCodec.SchemaVersion}). " +
                                "Client/server packs must match — reinstall both from the same 0.3.0 zip.");
                        }
                        catch { /* dedicated headless ok */ }
                    }
                    return false;
                }

                var flags = (IntentZdoCodec.IntentFlags)zdo.GetInt(KeyFlags, 0);
                if (!IntentZdoCodec.IsActive(flags))
                {
                    ReadsMiss++;
                    return false;
                }

                var writtenAt = zdo.GetFloat(KeyTime, 0f);
                if (!IntentZdoCodec.IsFresh(writtenAt, nowSeconds))
                {
                    ReadsStale++;
                    return false;
                }

                var focus = zdo.GetLong(KeyFocus, 0L);
                intent = new MemberIntent
                {
                    OrderKind = (DoctrineOrderKind)zdo.GetInt(KeyOrder, 0),
                    Formation = (FormationType)zdo.GetInt(KeyFormation, 0),
                    Stance = (StanceType)zdo.GetInt(KeyStance, 0),
                    Role = (SquadRole)zdo.GetInt(KeyRole, 0),
                    DesiredPosition = zdo.GetVec3(KeySlot, Vector3.zero),
                    FocusTargetId = focus != 0L ? focus : (long?)null,
                    SquadId = "",
                };
                IntentZdoCodec.ApplyFlags(intent, flags);
                ReadsOk++;
                return true;
            }
            catch (Exception ex)
            {
                FactionTacticsLog.Debug($"IntentZdoSync.TryRead failed: {ex.GetType().Name}: {ex.Message}");
                ReadsMiss++;
                return false;
            }
        }

        public static bool TryReadFromMonsterAI(MonsterAI ai, float nowSeconds, out MemberIntent intent)
        {
            intent = null!;
            return TryGetZdo(ai, out var zdo) && TryRead(zdo, nowSeconds, out intent);
        }

        public static void Clear(ZDO zdo)
        {
            if (zdo == null)
                return;
            try
            {
                zdo.Set(KeyFlags, 0);
                zdo.Set(KeyVersion, 0);
            }
            catch { /* ignore */ }
        }
#endif
    }
}
