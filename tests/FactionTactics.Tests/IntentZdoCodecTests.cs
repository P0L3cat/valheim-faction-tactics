using FactionTactics.Orders;
using Xunit;

namespace FactionTactics.Tests
{
    public class IntentZdoCodecTests
    {
        [Fact]
        public void PackFlags_round_trips_and_marks_active()
        {
            var intent = new MemberIntent
            {
                HoldGround = true,
                PreferRun = true,
                AllowVanillaChase = false,
                PreferKeepRange = true,
                AssaultWallBreaker = true,
                DeathRush = true,
            };
            var flags = IntentZdoCodec.PackFlags(intent);
            Assert.True(IntentZdoCodec.IsActive(flags));
            var again = new MemberIntent();
            IntentZdoCodec.ApplyFlags(again, flags);
            Assert.True(again.HoldGround);
            Assert.True(again.PreferRun);
            Assert.False(again.AllowVanillaChase);
            Assert.True(again.PreferKeepRange);
            Assert.True(again.AssaultWallBreaker);
            Assert.True(again.DeathRush);
        }

        [Fact]
        public void IsFresh_rejects_stale_and_accepts_recent()
        {
            Assert.False(IntentZdoCodec.IsFresh(0f, 10f));
            Assert.True(IntentZdoCodec.IsFresh(9f, 10f));
            Assert.False(IntentZdoCodec.IsFresh(1f, 10f));
        }

        [Fact]
        public void Product_and_schema_versions_are_1_0_5_v3()
        {
            Assert.Equal("1.0.10", FtVersion.ProductVersion);
            Assert.Equal(3, FtVersion.IntentSchemaVersion);
            Assert.Equal(IntentZdoCodec.SchemaVersion, FtVersion.IntentSchemaVersion);
        }
    }
}
