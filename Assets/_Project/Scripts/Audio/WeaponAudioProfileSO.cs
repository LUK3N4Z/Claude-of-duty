using UnityEngine;

namespace FPS.Audio
{
    /// <summary>
    /// A weapon's report as separate layers: sub-bass chest thump, muzzle blast transient, mechanical action, body
    /// resonance, the distant report heard far away, reverb tails for small rooms, warehouses, urban canyons and open
    /// ground, and the magazine's ping on its last rounds. Any clip can be a recorded WAV.
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponAudio", menuName = "FPS/Audio/Weapon Audio Profile")]
    public sealed class WeaponAudioProfileSO : ScriptableObject
    {
        [Header("Close Layers")]
        [Tooltip("Muzzle blast: the shock front and gas jet, 200 Hz - 8 kHz.")]
        [SerializeField] private AudioClip[] pressureTransients = new AudioClip[0];
        [Tooltip("Sub-bass chest thump, 30-90 Hz.")]
        [SerializeField] private AudioClip[] subBassThumps = new AudioClip[0];
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
        [Tooltip("Small room: sharp slapback, ~0.4 s decay.")]
        [SerializeField] private AudioClip indoorTail;
        [Tooltip("Warehouse or hall: booming metallic reverb, ~1.4 s decay.")]
        [SerializeField] private AudioClip warehouseTail;
        [Tooltip("Urban street canyon: flutter echo between facades, ~2.2 s decay.")]
        [SerializeField] private AudioClip urbanTail;
        [Tooltip("Open field: rolling low thunder off distant terrain, ~3 s decay.")]
        [SerializeField] private AudioClip openTail;

        [Header("Last Rounds")]
        [Tooltip("Magazine spring ping played on the final rounds as the follower rises.")]
        [SerializeField] private AudioClip lowAmmoPing;
        [Tooltip("The ping starts when fewer than this many rounds are left after a shot.")]
        [SerializeField, Min(0)] private int lowAmmoRounds = 3;

        public float DistantThreshold => distantThreshold;
        public float MaxAudibleDistance => maxAudibleDistance;
        public float Loudness => loudness;
        public AudioClip IndoorTail => indoorTail;
        public AudioClip WarehouseTail => warehouseTail;
        public AudioClip UrbanTail => urbanTail;
        public AudioClip OpenTail => openTail;
        public AudioClip LowAmmoPing => lowAmmoPing;
        public int LowAmmoRounds => lowAmmoRounds;

        public AudioClip PressureTransient => Pick(pressureTransients);
        public AudioClip SubBass => Pick(subBassThumps);
        public AudioClip MechanicalCycle => Pick(mechanicalCycles);
        public AudioClip BodyResonance => Pick(bodyResonances);
        public AudioClip DistantReport => Pick(distantReports);

        private static AudioClip Pick(AudioClip[] clips) => clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}
