using Shared.Enums;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>Property for Legion Y1 button remap (JSON ButtonMapping format).</summary>
    internal class LegionButtonY1Property : LegionButtonMappingProperty
    {
        public LegionButtonY1Property(ComboBox inUI, Page inOwner)
            : base(Function.LegionButtonY1, inUI, inOwner) { }
    }
}
