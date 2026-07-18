using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion M1 button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonM1Property : LegionButtonMappingProperty
    {
        public LegionButtonM1Property(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonM1, inUI, inOwner) { }
    }
}
