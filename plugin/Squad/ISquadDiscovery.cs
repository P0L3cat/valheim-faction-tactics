using System.Collections.Generic;

namespace FactionTactics.Squad
{
    /// <summary>Discovers and clusters faction allies into squads.</summary>
    public interface ISquadDiscovery
    {
        IReadOnlyList<SquadUnit> Discover();
    }
}
