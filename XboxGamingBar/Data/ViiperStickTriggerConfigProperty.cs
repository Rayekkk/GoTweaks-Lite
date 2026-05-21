using Shared.Enums;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Widget-side property holding the serialized stick / trigger
    /// shaping bundle (LS, RS, LT, RT). The UI controls in
    /// GamingWidget.xaml don't bind individually — code-behind reads
    /// every control, rebuilds a <see cref="Shared.Data.StickTriggerConfigBundle"/>,
    /// serializes it to this property's string value, which then
    /// auto-syncs to the helper via the standard pipe + LocalSettings
    /// (Function.Viiper_StickTriggerConfig).
    /// </summary>
    internal class ViiperStickTriggerConfigProperty : WidgetProperty<string>
    {
        public ViiperStickTriggerConfigProperty(string initialValue)
            : base(initialValue ?? string.Empty, null, Function.Viiper_StickTriggerConfig)
        {
        }
    }
}
