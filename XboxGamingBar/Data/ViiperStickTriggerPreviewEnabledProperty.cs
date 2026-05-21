using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget→helper toggle for the VIIPER Sticks &amp; Triggers live
    /// preview stream. Set to true when the panel is expanded so the
    /// helper starts streaming raw samples; false on collapse / widget
    /// unload to silence the stream.
    /// </summary>
    internal class ViiperStickTriggerPreviewEnabledProperty : WidgetProperty<bool>
    {
        public ViiperStickTriggerPreviewEnabledProperty()
            : base(false, null, Function.Viiper_StickTriggerPreviewEnabled)
        {
        }
    }
}
