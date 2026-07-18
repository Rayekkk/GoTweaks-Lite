using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion M2 button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonM2Property : LegionButtonMappingProperty
    {
        public LegionButtonM2Property(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonM2, inUI, inOwner) { }
    }
}
