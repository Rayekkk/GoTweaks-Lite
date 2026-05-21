using Shared.Enums;
using XboxGamingBarHelper.Core;

namespace XboxGamingBarHelper.ControllerEmulation.Viiper
{
    /// <summary>
    /// Helper→widget live-input telemetry for the VIIPER Sticks &amp; Triggers
    /// panel. <see cref="ViiperInputForwarder"/> writes the latest raw
    /// XInput frame (sticks + triggers) as a flat
    /// <c>"LX,LY,RX,RY,LT,RT"</c> string at ~30 Hz whenever the widget has
    /// set <see cref="Function.Viiper_StickTriggerPreviewEnabled"/> to true.
    /// Widget side runs <see cref="Shared.Data.StickTriggerProcessor"/>
    /// against the current bundle to compute the shaped values, so a single
    /// 6-number sample drives both the raw and shaped indicators in the
    /// canvas without doubling the bandwidth.
    /// </summary>
    internal class ViiperStickTriggerLiveSampleProperty : HelperProperty<string, ViiperEmulationManager>
    {
        public ViiperStickTriggerLiveSampleProperty(ViiperEmulationManager manager)
            : base(string.Empty, null, Function.Viiper_StickTriggerLiveSample, manager)
        {
        }
    }
}
