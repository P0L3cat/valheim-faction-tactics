using FactionTactics.Orders;
using FactionTactics.Squad;

namespace FactionTactics.Commander
{
    /// <summary>
    /// Route 3 seam: propose a <see cref="SquadOrder"/> from a snapshot.
    /// v0 = ScriptedCommander; later = LlmCommander (same DTO, no model code here).
    /// </summary>
    public interface ICommander
    {
        SquadOrder? Propose(SquadSnapshot snapshot);
    }
}
