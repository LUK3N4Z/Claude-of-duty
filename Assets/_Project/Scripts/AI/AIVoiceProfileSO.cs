using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS.AI
{
    /// <summary>One soldier voice: the clips shouted for each callout.</summary>
    [CreateAssetMenu(fileName = "Voice", menuName = "FPS/AI/Voice Profile")]
    public sealed class AIVoiceProfileSO : ScriptableObject
    {
        [Serializable]
        public struct Line
        {
            public CalloutType callout;
            public AudioClip[] clips;
        }

        [SerializeField] private Line[] lines = new Line[0];
        [Tooltip("Shout loudness; callouts carry about this far before they fade out (m).")]
        [SerializeField, Min(1f)] private float audibleDistance = 45f;

        public float AudibleDistance => audibleDistance;

        /// <summary>The words of each callout, as written for voice recording.</summary>
        public static string Text(CalloutType callout)
        {
            switch (callout)
            {
                case CalloutType.Contact: return "Contact!";
                case CalloutType.Reloading: return "Reloading!";
                case CalloutType.Covering: return "Covering!";
                case CalloutType.FlankingLeft: return "Flanking left!";
                case CalloutType.FlankingRight: return "Flanking right!";
                case CalloutType.TargetSuppressed: return "Target suppressed!";
                case CalloutType.FragOut: return "Frag out!";
                case CalloutType.FlashbangOut: return "Flashbang out!";
                case CalloutType.StackUp: return "Stack up on the door!";
                case CalloutType.Breaching: return "Go, go, go!";
                case CalloutType.Clear: return "Clear!";
                case CalloutType.LostContact: return "Lost contact!";
                case CalloutType.ManDown: return "Man down!";
                default: return callout.ToString();
            }
        }

        /// <summary>A random take of <paramref name="callout"/>, or null when the voice has none.</summary>
        public AudioClip Pick(CalloutType callout)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].callout != callout) continue;
                AudioClip[] clips = lines[i].clips;
                return clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;
            }

            return null;
        }
    }
}
