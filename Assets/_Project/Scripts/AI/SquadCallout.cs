using UnityEngine;

namespace FPS.AI
{
    public enum CalloutType
    {
        Contact,
        Reloading,
        Covering,
        FlankingLeft,
        FlankingRight,
        TargetSuppressed,
        FragOut,
        FlashbangOut,
        StackUp,
        Breaching,
        Clear,
        LostContact,
        ManDown,
    }

    /// <summary>Raised on <c>EventBus</c> whenever a soldier shouts a callout (audio is played by the speaker).</summary>
    public struct SquadCalloutEvent
    {
        public Transform Speaker;
        public CalloutType Type;
        public Vector3 Position;
    }
}
