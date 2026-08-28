#nullable enable

using UnityEngine;

namespace DeathHeadHopperFix.API.Abilities
{
    public struct AbilitySlotState
    {
        public bool Visible { get; set; }
        public bool Available { get; set; }
        public float ActivationProgress01 { get; set; }
        public string? Label { get; set; }
        public Sprite? Icon { get; set; }
    }
}
