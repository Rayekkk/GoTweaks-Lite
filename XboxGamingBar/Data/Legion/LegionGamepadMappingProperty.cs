using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for Legion Gamepad Button Mapping (JSON string dictionary, e.g. the Nintendo-layout
    /// and Desktop-Controls presets' face/D-pad/stick-click remaps).
    ///
    /// [2.0 rebuild - slice 8] Helper-authoritative. No UI is bound to this property directly (the
    /// standalone "Gamepad Buttons" arbitrary-remap section was removed, §29) - a helper push just
    /// updates the in-memory gamepadButtonMappings dict via OnValueSyncedFromHelper. User edits (from
    /// the Nintendo/Desktop preset toggles) still send via SaveAndSendGamepadMappings.
    /// </summary>
    internal class LegionGamepadMappingProperty : WidgetProperty<string>
    {
        private readonly GamingWidget owner;

        public LegionGamepadMappingProperty(GamingWidget inOwner) : base("", null, Function.LegionGamepadButtonMapping)
        {
            owner = inOwner;
        }

        protected override void OnValueSyncedFromHelper()
        {
            if (owner == null) return;

            // gamepadButtonMappings is a plain Dictionary read/written elsewhere only on the UI
            // thread (button handlers, ApplyControllerProfile) - dispatch the mutation to avoid a
            // cross-thread race, matching LegionButtonMappingProperty's same-shaped hook.
            string json = Value;
            var ignore = owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () => owner.ApplyGamepadButtonMappingsFromHelper(json));
        }
    }
}
