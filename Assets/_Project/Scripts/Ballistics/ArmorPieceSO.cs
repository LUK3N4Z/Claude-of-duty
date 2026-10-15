using UnityEngine;

namespace FPS.Ballistics
{
    public enum ArmorCoverage
    {
        /// <summary>Thorax and stomach, against shots arriving from the front.</summary>
        FrontPlate,
        /// <summary>Thorax and stomach, against shots arriving from behind.</summary>
        BackPlate,
        /// <summary>Head, except straight-on shots to the face.</summary>
        Helmet,
    }

    /// <summary>A plate or helmet: protection rating against a bullet's penetration power, durability, blunt trauma.</summary>
    [CreateAssetMenu(fileName = "Armor", menuName = "FPS/Ballistics/Armor Piece")]
    public sealed class ArmorPieceSO : ScriptableObject
    {
        [SerializeField] private string displayName = "Ceramic Plate";
        [SerializeField] private ArmorCoverage coverage = ArmorCoverage.FrontPlate;
        [Tooltip("Compared with the bullet's energy-scaled penetration power (0-100).")]
        [SerializeField, Range(0f, 100f)] private float protectionRating = 60f;
        [SerializeField, Min(1f)] private float maxDurability = 80f;
        [Tooltip("Share of the hit's damage felt as blunt trauma when the armor stops it.")]
        [SerializeField, Range(0f, 1f)] private float bluntTransfer = 0.15f;
        [Tooltip("Share of damage still absorbed when the bullet does get through.")]
        [SerializeField, Range(0f, 1f)] private float residualAbsorption = 0.2f;
        [SerializeField, Min(0f)] private float massKg = 3.2f;

        public string DisplayName => displayName;
        public ArmorCoverage Coverage => coverage;
        public float ProtectionRating => protectionRating;
        public float MaxDurability => maxDurability;
        public float BluntTransfer => bluntTransfer;
        public float ResidualAbsorption => residualAbsorption;
        public float MassKg => massKg;
    }
}
