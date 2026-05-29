using System;

namespace XboxGamingBarHelper.Labs
{
    /// <summary>
    /// Stable identifiers for every physical Legion Go button the press-edge detector
    /// reports. Shared by the reactive-lighting and GoTweaks-haptics features so both
    /// react to the same event stream. Values are arbitrary but stable (persisted in
    /// per-button settings), so do not renumber existing entries.
    /// </summary>
    internal enum LegionInputButton
    {
        None = 0,

        // Face buttons (ABXY group)
        A = 1,
        B = 2,
        X = 3,
        Y = 4,

        // Front buttons / system (front group)
        DpadUp = 10,
        DpadDown = 11,
        DpadLeft = 12,
        DpadRight = 13,
        Start = 14,
        Back = 15,
        LeftThumb = 16,
        RightThumb = 17,
        LeftShoulder = 18,
        RightShoulder = 19,
        Mode = 20,
        Share = 21,

        // Back paddles / extra remappable buttons (back group)
        ExtraL1 = 30,
        ExtraL2 = 31,
        ExtraR1 = 32,
        ExtraRM1 = 33,
        ExtraR2 = 34,
        ExtraR3 = 35,

        // Analog triggers crossing the digital threshold (trigger group)
        LeftTrigger = 40,
        RightTrigger = 41,
    }

    /// <summary>
    /// Logical grouping used by the GoTweaks Haptics UI (ABXY / front / back / triggers)
    /// so haptics can be enabled and tuned per group rather than per individual button.
    /// </summary>
    internal enum LegionButtonGroup
    {
        None = 0,
        Face = 1,     // ABXY
        Front = 2,    // dpad, start/back, thumbs, shoulders, mode/share
        Back = 3,     // back paddles / extra remappable
        Trigger = 4,  // LT/RT
    }

    /// <summary>
    /// A button-down (press-edge) event from the Legion HID layer. <see cref="Pressed"/>
    /// is true on the rising edge and false on release, so consumers can drive both
    /// flash-on-press and hold-style effects.
    /// </summary>
    internal sealed class LegionButtonEdgeEventArgs : EventArgs
    {
        public readonly LegionInputButton Button;
        public readonly LegionButtonGroup Group;
        public readonly bool Pressed;

        public LegionButtonEdgeEventArgs(LegionInputButton button, LegionButtonGroup group, bool pressed)
        {
            Button = button;
            Group = group;
            Pressed = pressed;
        }
    }

    internal static class LegionButtonClassifier
    {
        public static LegionButtonGroup GroupOf(LegionInputButton b)
        {
            switch (b)
            {
                case LegionInputButton.A:
                case LegionInputButton.B:
                case LegionInputButton.X:
                case LegionInputButton.Y:
                    return LegionButtonGroup.Face;

                case LegionInputButton.ExtraL1:
                case LegionInputButton.ExtraL2:
                case LegionInputButton.ExtraR1:
                case LegionInputButton.ExtraRM1:
                case LegionInputButton.ExtraR2:
                case LegionInputButton.ExtraR3:
                    return LegionButtonGroup.Back;

                case LegionInputButton.LeftTrigger:
                case LegionInputButton.RightTrigger:
                    return LegionButtonGroup.Trigger;

                case LegionInputButton.None:
                    return LegionButtonGroup.None;

                default:
                    return LegionButtonGroup.Front;
            }
        }
    }
}
