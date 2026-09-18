using FactionTactics.Doctrine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Route 2 hook: optional utility score when selecting doctrine orders.
    /// Default implementation is <see cref="NullActionScorer"/>.
    /// </summary>
    public interface IActionScorer
    {
        /// <summary>
        /// Score a candidate order. NullScorer returns 0 so ScriptedCommander/doctrine FSM wins.
        /// </summary>
        float ScoreAction(DoctrineOrderKind order, SquadSnapshot snapshot);
    }
}
