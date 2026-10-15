using UnityEngine;

namespace FPS.Audio
{
    /// <summary>
    /// A weapon's report as separate layers: close pressure transient, mechanical action, body resonance, the distant
    /// report heard far away, and reverb tails for indoor rooms, urban canyons and open ground.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponAudio", menuName = "FPS/Audio/Weapon Audio Profile")]
    public sealed class WeaponAudioProfileSO : ScriptableObject
    {
        [Header("Close Layers")]
        [SerializeField] private AudioClip[] pressureTransients = new AudioClip[0];
        [SerializeField] private AudioClip[] mechanicalCycles = new AudioClip[0];
        [SerializeField] private AudioClip[] bodyResonances = new AudioClip[0];

        [Header("Distance")]
        [SerializeField] private AudioClip[] distantReports = new AudioClip[0];
        [Tooltip("Beyond this distance listeners hear the distant report instead of the close layers (m).")]
        [SerializeField, Min(1f)] private float distantThreshold = 120f;
        [Tooltip("Farthest a shot is heard at all (m).")]
        [SerializeField, Min(10f)] private float maxAudibleDistance = 1500f;
        [SerializeField, Range(0f, 1f)] private float loudness = 1f;

        [Header("Reverb Tails")]
        [SerializeField] private AudioClip indoorTail;
        [SerializeField] private AudioClip urbanTail;
        [SerializeField] private AudioClip openTail;

        public float DistantThreshold => distantThreshold;
        public float MaxAudibleDistance => maxAudibleDistance;
        public float Loudness => loudness;
        public AudioClip IndoorTail => indoorTail;
        public AudioClip UrbanTail => urbanTail;
        public AudioClip OpenTail => openTail;

        public AudioClip PressureTransient => Pick(pressureTransients);
        public AudioClip MechanicalCycle => Pick(mechanicalCycles);
        public AudioClip BodyResonance => Pick(bodyResonances);
        public AudioClip DistantReport => Pick(distantReports);

        private static AudioClip Pick(AudioClip[] clips) => clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}
