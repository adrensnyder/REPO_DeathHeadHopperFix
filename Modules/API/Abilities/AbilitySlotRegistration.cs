#nullable enable

using System;
using UnityEngine;

namespace DeathHeadHopperFix.API.Abilities
{
    public sealed class AbilitySlotRegistration
    {
        public string OwnerId { get; set; } = string.Empty;
        public ExtensibleAbilitySlot Slot { get; set; }
        public string AbilityName { get; set; } = string.Empty;
        public Sprite? Icon { get; set; }
        public Action? OnDown { get; set; }
        public Action? OnHold { get; set; }
        public Action? OnUp { get; set; }
        public Action? OnCancel { get; set; }
    }
}
