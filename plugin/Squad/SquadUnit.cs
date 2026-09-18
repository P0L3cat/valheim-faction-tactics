using System.Collections.Generic;
using FactionTactics.Doctrine;
using FactionTactics.Orders;

namespace FactionTactics.Squad
{
    /// <summary>Runtime squad cluster under one doctrine pack.</summary>
    public sealed class SquadUnit
    {
        public string SquadId { get; set; } = "";
        public IDoctrinePack Doctrine { get; set; } = null!;
        public List<SquadMemberView> Members { get; } = new List<SquadMemberView>();
        public SquadOrder? CurrentOrder { get; set; }
        public DoctrineOrderKind? PreviousOrderKind { get; set; }
        public float AgeSeconds { get; set; }
    }
}
