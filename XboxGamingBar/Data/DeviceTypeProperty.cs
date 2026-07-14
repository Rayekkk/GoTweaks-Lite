using Shared.Enums;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace XboxGamingBar.Data
{
    // Read-only status pushed from the helper: the detected device type (Shared.Enums.DeviceType,
    // as its raw int value). Static for the process lifetime - only applied once, since device
    // identity never changes at runtime. Headless (no bound control) - currently used only to
    // show/hide the Auto SDR section, which is Legion Go 2 only.
    internal class DeviceTypeProperty : WidgetProperty<int>
    {
        private readonly GamingWidget owner;

        public DeviceTypeProperty(int inValue, GamingWidget inOwner) : base(inValue, null, Function.DeviceType)
        {
            owner = inOwner;
        }

        protected override void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);
            Apply();
        }

        public override Task OnBatchSyncCompleted()
        {
            Apply();
            return Task.CompletedTask;
        }

        private void Apply()
        {
            if (owner == null) return;
            var value = Value;
            _ = owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => owner.ApplyDeviceTypeGate(value));
        }
    }
}
