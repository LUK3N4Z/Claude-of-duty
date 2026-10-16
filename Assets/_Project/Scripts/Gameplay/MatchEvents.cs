namespace FPS.Gameplay
{
    public enum MatchPhase
    {
        Waiting,
        /// <summary>Everyone placed; the round goes live after a short countdown.</summary>
        PreRound,
        Live,
        RoundOver,
        MatchOver,
    }

    /// <summary>Raised on <c>EventBus</c> whenever the match changes phase.</summary>
    public struct MatchPhaseEvent
    {
        public MatchPhase Phase;
        public int Round;
        /// <summary>Winning team of the round or match; -1 while undecided or drawn.</summary>
        public int Winner;
        public string Message;
    }
}
