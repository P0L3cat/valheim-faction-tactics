using FactionTactics.Doctrine;

namespace FactionTactics.Squad
{
    public sealed class NullRoleScorer : IRoleScorer
    {
        public float ScoreRole(SquadMemberView member, SquadRole role, SquadSnapshot snapshot) => 0f;
    }

    public sealed class NullActionScorer : IActionScorer
    {
        public float ScoreAction(DoctrineOrderKind order, SquadSnapshot snapshot) => 0f;
    }
}
