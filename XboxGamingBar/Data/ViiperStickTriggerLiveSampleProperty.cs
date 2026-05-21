using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side mirror of the helper's raw stick/trigger telemetry
    /// stream. Receives ~30 Hz frames as flat <c>"LX,LY,RX,RY,LT,RT"</c>
    /// strings while the preview is on. The code-behind parses the value,
    /// runs <see cref="Shared.Data.StickTriggerProcessor"/> against the
    /// current bundle to derive shaped values, and updates the canvas in
    /// place — one push from the helper drives both raw and shaped
    /// indicators without doubling pipe bandwidth.
    /// </summary>
    internal class ViiperStickTriggerLiveSampleProperty : WidgetProperty<string>
    {
        public ViiperStickTriggerLiveSampleProperty()
            : base(string.Empty, null, Function.Viiper_StickTriggerLiveSample)
        {
        }
    }
}
