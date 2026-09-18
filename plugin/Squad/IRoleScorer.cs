using FactionTactics.Doctrine;

namespace FactionTactics.Squad
{
    /// <summary>
    /// Route 2 hook: optional utility score when assigning roles.
    /// Default implementation is <see cref="NullRoleScorer"/>.
    /// </summary>
    public interface IRoleScorer
    {
        /// <summary>
        /// Returns a score for assigning <paramref name="role"/> to <paramref name="member"/>.
        /// Higher is better. NullScorer always returns 0 (FSM/doctrine decides alone).
        /// </summary>
        float ScoreRole(SquadMemberView member, SquadRole role, SquadSnapshot snapshot);
    }
}
