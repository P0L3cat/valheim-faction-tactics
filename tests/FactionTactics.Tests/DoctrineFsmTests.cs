using System.Linq;
using FactionTactics.Doctrine;
using FactionTactics.Orders;
using Xunit;

namespace FactionTactics.Tests
{
    public class DoctrineFsmTests
    {
        public DoctrineFsmTests() => TestConfig.EnsureBound();

        [Fact]
        public void Roman_Hold_Advance_FocusFire_no_early_Charge_with_missiles()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var seq = FakeSnapshots.DriveFsm(cmd, FakeSnapshots.Roman(), 5, (snap, prev) =>
            {
                // far → wall band → close (still with missiles) — must not Charge
                if (prev == null)
                    return FakeSnapshots.WithThreat(snap, 40f);
                if (prev == DoctrineOrderKind.Hold || prev == DoctrineOrderKind.Advance)
                    return FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 16f);
                if (prev == DoctrineOrderKind.FocusFire || prev == DoctrineOrderKind.ProtectMissiles)
                    return FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 5f);
                return FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 3f);
            });

            Assert.Contains(DoctrineOrderKind.Advance, seq);
            Assert.Contains(seq, k => k == DoctrineOrderKind.FocusFire
                                      || k == DoctrineOrderKind.ProtectMissiles
                                      || k == DoctrineOrderKind.Hold);
            Assert.DoesNotContain(DoctrineOrderKind.Charge, seq); // missiles present → no Charge
            Assert.DoesNotContain(DoctrineOrderKind.Kite, seq); // Roman does not kite
        }

        [Fact]
        public void Roman_does_not_charge_at_8m_holds_missile_line()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var mid = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 8f);
            mid.PreviousOrderKind = nameof(DoctrineOrderKind.Advance);
            var order = cmd.Propose(mid);
            Assert.True(order!.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles);
            Assert.NotEqual(DoctrineOrderKind.Charge, order.OrderKind);
        }

        [Fact]
        public void Roman_no_missiles_advances_then_holds_PreferRanged_no_default_Charge()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var snap = FakeSnapshots.WithCombatRoles(FakeSnapshots.Base("roman", 4), front: 3, missile: 0, flanker: 0, leader: 1);

            var far = FakeSnapshots.WithThreat(snap, 20f);
            Assert.Equal(DoctrineOrderKind.Advance, cmd.Propose(far)!.OrderKind);

            var holdLine = FakeSnapshots.WithThreat(
                FakeSnapshots.WithCombatRoles(FakeSnapshots.Base("roman", 4), front: 3, missile: 0, flanker: 0, leader: 1),
                10f);
            holdLine.PreviousOrderKind = nameof(DoctrineOrderKind.Advance);
            Assert.Equal(DoctrineOrderKind.Hold, cmd.Propose(holdLine)!.OrderKind);

            // PreferRanged default: inside charge band with no missiles → still Hold (no default Charge)
            var close = FakeSnapshots.WithThreat(
                FakeSnapshots.WithCombatRoles(FakeSnapshots.Base("roman", 4), front: 3, missile: 0, flanker: 0, leader: 1),
                3f);
            close.PreviousOrderKind = nameof(DoctrineOrderKind.Hold);
            close.CasualtyRatio = 0.1f;
            Assert.Equal(DoctrineOrderKind.Hold, cmd.Propose(close)!.OrderKind);

            // Last-resort casualties → Charge allowed under PreferRanged
            var lastResort = FakeSnapshots.WithThreat(
                FakeSnapshots.WithCombatRoles(FakeSnapshots.Base("roman", 4), front: 3, missile: 0, flanker: 0, leader: 1),
                3f);
            lastResort.PreviousOrderKind = nameof(DoctrineOrderKind.Hold);
            lastResort.CasualtyRatio = 0.4f;
            Assert.Equal(DoctrineOrderKind.Charge, cmd.Propose(lastResort)!.OrderKind);

            // 5m still outside charge band → Hold wall
            var stillHold = FakeSnapshots.WithThreat(
                FakeSnapshots.WithCombatRoles(FakeSnapshots.Base("roman", 4), front: 3, missile: 0, flanker: 0, leader: 1),
                5f);
            stillHold.PreviousOrderKind = nameof(DoctrineOrderKind.Hold);
            Assert.Equal(DoctrineOrderKind.Hold, cmd.Propose(stillHold)!.OrderKind);
        }

        [Fact]
        public void Roman_charge_hysteresis_drops_when_missiles_can_shoot()
        {
            var cmd = FakeSnapshots.CreateCommander();
            // Previous Charge but missiles present and not last-resort → drop to FocusFire/ProtectMissiles
            var stay = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 4f);
            stay.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            var order = cmd.Propose(stay);
            Assert.NotEqual(DoctrineOrderKind.Charge, order!.OrderKind);
            Assert.True(order.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles
                        || order.OrderKind == DoctrineOrderKind.Hold);

            var fled = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 20f);
            fled.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            Assert.Equal(DoctrineOrderKind.Advance, cmd.Propose(fled)!.OrderKind);
        }

        [Fact]
        public void Roman_no_Charge_at_3m_when_missiles_present()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var close = FakeSnapshots.WithThreat(FakeSnapshots.Roman(), 3f);
            close.PreviousOrderKind = nameof(DoctrineOrderKind.FocusFire);
            close.CasualtyRatio = 0.1f;
            var order = cmd.Propose(close);
            Assert.NotEqual(DoctrineOrderKind.Charge, order!.OrderKind);
            Assert.True(order.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.ProtectMissiles
                        || order.OrderKind == DoctrineOrderKind.Hold);
        }

        [Fact]
        public void Formation_facing_basis_orients_toward_threat()
        {
            var centroid = new UnityEngine.Vector3(0f, 0f, 0f);
            var threat = new UnityEngine.Vector3(10f, 0f, 0f);
            OrderApplicator.BuildFacingBasis(centroid, threat, out var right, out var forward);
            Assert.True(UnityEngine.Vector3.Dot(forward, new UnityEngine.Vector3(1f, 0f, 0f)) > 0.99f);
            Assert.True(UnityEngine.Vector3.Dot(right, new UnityEngine.Vector3(0f, 0f, -1f)) > 0.99f);
        }

        [Fact]
        public void Ambush_lurk_then_encircle_then_kite_after_charge()
        {
            var cmd = FakeSnapshots.CreateCommander();
            // Outside pocket → Hold
            var hold = cmd.Propose(FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 30f));
            Assert.Equal(DoctrineOrderKind.Hold, hold!.OrderKind);
            Assert.Equal(FormationType.Orb, hold.Formation); // Orb primacy (0.2)

            // Enter pocket mid (8–18f) → FocusFire or Flank (Orb encircle)
            var mid = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 12f);
            mid.PreviousOrderKind = nameof(DoctrineOrderKind.Hold);
            var midOrder = cmd.Propose(mid);
            Assert.True(midOrder!.OrderKind == DoctrineOrderKind.FocusFire
                        || midOrder.OrderKind == DoctrineOrderKind.Flank
                        || midOrder.OrderKind == DoctrineOrderKind.ProtectMissiles);
            Assert.Equal(FormationType.Orb, midOrder.Formation);

            // After Charge → Kite (harassment loop, not RetreatAndReform)
            var afterCharge = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 8f);
            afterCharge.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            var kite = cmd.Propose(afterCharge);
            Assert.Equal(DoctrineOrderKind.Kite, kite!.OrderKind);
            Assert.Equal(FormationType.Orb, kite.Formation);
        }

        [Fact]
        public void Ambush_no_immediate_Charge_from_Flank_without_isolate()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 6f);
            snap.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            snap.AgeSeconds = 0.5f; // Flank too young for envelope flash
            snap.TargetIsolated = false;
            snap.ThreatStaggeredOrLow = false;
            var order = cmd.Propose(snap);
            Assert.NotEqual(DoctrineOrderKind.Charge, order!.OrderKind);
            Assert.True(order.OrderKind == DoctrineOrderKind.Flank
                        || order.OrderKind == DoctrineOrderKind.Kite);
        }

        [Fact]
        public void Ambush_Charge_only_when_isolated_from_Flank()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var snap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 6f);
            snap.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            snap.AgeSeconds = 0.5f;
            snap.TargetIsolated = true;
            Assert.Equal(DoctrineOrderKind.Charge, cmd.Propose(snap)!.OrderKind);
        }

        [Fact]
        public void Ambush_Charge_then_Kite_harassment_loop()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var after = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 4f);
            after.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            Assert.Equal(DoctrineOrderKind.Kite, cmd.Propose(after)!.OrderKind);

            // After Kite with gap > 14f → re-encircle Flank
            var gap = FakeSnapshots.WithThreat(FakeSnapshots.Ambush(), 15f);
            gap.PreviousOrderKind = nameof(DoctrineOrderKind.Kite);
            Assert.Equal(DoctrineOrderKind.Flank, cmd.Propose(gap)!.OrderKind);
        }

        [Fact]
        public void Viking_shield_wall_holds_then_charges_close()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var far = cmd.Propose(FakeSnapshots.WithThreat(FakeSnapshots.Viking(), 40f));
            Assert.Equal(DoctrineOrderKind.Advance, far!.OrderKind);

            var close = FakeSnapshots.WithThreat(FakeSnapshots.Viking(), 8f);
            close.PreviousOrderKind = nameof(DoctrineOrderKind.FocusFire);
            var charge = cmd.Propose(close);
            Assert.Equal(DoctrineOrderKind.Charge, charge!.OrderKind);

            var after = FakeSnapshots.WithThreat(FakeSnapshots.Viking(), 8f);
            after.PreviousOrderKind = nameof(DoctrineOrderKind.Charge);
            Assert.Equal(DoctrineOrderKind.RetreatAndReform, cmd.Propose(after)!.OrderKind);
        }

        [Fact]
        public void Steppe_kites_when_pressed_open_field()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var pressed = FakeSnapshots.WithThreat(FakeSnapshots.Steppe(), 5f);
            pressed.NearStructure = false;
            var order = cmd.Propose(pressed);
            Assert.Equal(DoctrineOrderKind.Kite, order!.OrderKind);
        }

        [Fact]
        public void InsectSiege_softens_near_Dvergr()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var soft = FakeSnapshots.WithThreat(FakeSnapshots.InsectSiege(), 20f);
            soft.NearDvergr = true;
            soft.NearbyDvergrCount = 2;
            var order = cmd.Propose(soft);
            Assert.True(order!.OrderKind == DoctrineOrderKind.Hold
                        || order.OrderKind == DoctrineOrderKind.FocusFire
                        || order.OrderKind == DoctrineOrderKind.Kite);
            Assert.NotEqual(DoctrineOrderKind.Charge, order.OrderKind);
        }

        [Fact]
        public void Charred_advances_then_may_flank_or_charge()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var far = cmd.Propose(FakeSnapshots.WithThreat(FakeSnapshots.Charred(), 40f));
            Assert.Equal(DoctrineOrderKind.Advance, far!.OrderKind);

            var mid = FakeSnapshots.WithThreat(FakeSnapshots.Charred(), 18f);
            mid.FlankOpportunity = true;
            var midOrder = cmd.Propose(mid);
            Assert.True(midOrder!.OrderKind == DoctrineOrderKind.FocusFire
                        || midOrder.OrderKind == DoctrineOrderKind.ProtectMissiles
                        || midOrder.OrderKind == DoctrineOrderKind.Flank
                        || midOrder.OrderKind == DoctrineOrderKind.Advance);
        }

        [Fact]
        public void PackHunters_flank_or_focus_then_charge_isolated()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var mid = FakeSnapshots.WithThreat(FakeSnapshots.PackHunters(), 20f);
            var midOrder = cmd.Propose(mid);
            Assert.True(midOrder!.OrderKind == DoctrineOrderKind.FocusFire
                        || midOrder.OrderKind == DoctrineOrderKind.Flank
                        || midOrder.OrderKind == DoctrineOrderKind.Advance);

            var iso = FakeSnapshots.WithThreat(FakeSnapshots.PackHunters(), 8f);
            iso.TargetIsolated = true;
            iso.PreviousOrderKind = nameof(DoctrineOrderKind.Flank);
            Assert.Equal(DoctrineOrderKind.Charge, cmd.Propose(iso)!.OrderKind);
        }

        [Fact]
        public void ArtilleryJelly_never_charges_and_kites_when_close()
        {
            var cmd = FakeSnapshots.CreateCommander();
            var seq = FakeSnapshots.DriveFsm(cmd, FakeSnapshots.ArtilleryJelly(), 6, (snap, prev) =>
            {
                // close band then comfort
                if (prev == null)
                    return FakeSnapshots.WithThreat(FakeSnapshots.ArtilleryJelly(), 6f);
                if (prev == DoctrineOrderKind.Kite)
                    return FakeSnapshots.WithThreat(FakeSnapshots.ArtilleryJelly(), 15f);
                return FakeSnapshots.WithThreat(FakeSnapshots.ArtilleryJelly(), 30f);
            });

            Assert.DoesNotContain(DoctrineOrderKind.Charge, seq);
            Assert.Contains(DoctrineOrderKind.Kite, seq);
        }

        [Fact]
        public void No_threat_yields_Hold_for_all_factions()
        {
            var cmd = FakeSnapshots.CreateCommander();
            foreach (var id in FakeSnapshots.ExpectedDoctrineIds)
            {
                var snap = FakeSnapshots.Base(id);
                snap.ThreatCount = 0;
                var order = cmd.Propose(snap);
                Assert.NotNull(order);
                Assert.Equal(DoctrineOrderKind.Hold, order!.OrderKind);
            }
        }
    }
}
