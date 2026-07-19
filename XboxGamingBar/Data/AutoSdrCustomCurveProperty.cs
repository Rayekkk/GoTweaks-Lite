using Shared.Enums;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace XboxGamingBar.Data
{
    // The Custom preset's brightness->SDR curve, as Go2HDR-compatible flat JSON. Headless (no
    // single bound control) - drives the chart + editable row list, which live across several
    // controls at once. Owner-driven redraw lives on GamingWidget
    // (ApplyAutoSdrCustomCurve, in GamingWidget.AutoSdrCurve.cs).
    internal class AutoSdrCustomCurveProperty : WidgetProperty<string>
    {
        private readonly GamingWidget owner;

        public AutoSdrCustomCurveProperty(string inValue, GamingWidget inOwner) : base(inValue, null, Function.AutoSdrCustomCurve)
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

        protected override async Task OnSetRequestPendingChanged(bool pending)
        {
            if (owner != null)
                await owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () => owner.SetAutoSdrCurveStatus(pending ? "Applying..." : string.Empty));
        }

        protected override async Task OnSetRequestFailed(string reason)
        {
            if (owner != null)
                await owner.ShowSettingApplyFailureAsync(Function.AutoSdrCustomCurve, reason);
        }

        private void Apply()
        {
            if (owner == null) return;
            var value = Value;
            _ = owner.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => owner.ApplyAutoSdrCustomCurve(value));
        }
    }
}
