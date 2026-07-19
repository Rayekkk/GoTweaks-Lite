using Shared.Enums;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.UI.Xaml.Controls;

namespace XboxGamingBar.Data
{
    /// <summary>
    /// Property for the GPD software fan curve graph. Manages 10 fan speed values as a comma-separated string.
    /// Values are fan speed percentages (0-100) for temperature thresholds 30-100°C.
    /// Includes debouncing to avoid sending too many updates during drag operations.
    /// </summary>
    internal class GPDFanCurveGraphProperty : WidgetProperty<string>
    {
        private readonly Page _owner;
        private Action<int[]> _graphUpdateCallback;
        private Action<bool> _requestResultCallback;
        private Timer _debounceTimer;
        private readonly SemaphoreSlim _requestGate = new SemaphoreSlim(1, 1);
        private string _pendingValue;
        private readonly object _debounceLock = new object();
        private const int DEBOUNCE_MS = 500;

        // Default fan curve values (GPD defaults - conservative ramp)
        public static readonly int[] DefaultCurve = { 0, 30, 35, 45, 55, 65, 75, 85, 95, 100 };

        // Temperature thresholds: 30°C to 100°C in ~7.8°C steps (10 points)
        public static readonly int[] Temperatures = { 30, 38, 46, 54, 62, 70, 78, 86, 94, 100 };

        public GPDFanCurveGraphProperty(Page owner)
            : base(FormatCurveData(DefaultCurve), null, Function.GPDFanCurveData)
        {
            _owner = owner;
        }

        /// <summary>
        /// Sets a callback to update the graph UI when values change from helper
        /// </summary>
        public void SetGraphUpdateCallback(Action<int[]> callback)
        {
            _graphUpdateCallback = callback;
        }

        public void SetRequestResultCallback(Action<bool> callback)
        {
            _requestResultCallback = callback;
        }

        /// <summary>
        /// Converts the string value to an array of integers
        /// </summary>
        public int[] GetCurveValues()
        {
            return ParseCurveData(Value);
        }

        /// <summary>
        /// Sets curve values from the graph UI with debouncing
        /// </summary>
        public void SetCurveValuesDebounced(int[] values)
        {
            if (values == null || values.Length != 10)
            {
                Logger.Warn("Invalid GPD curve values array");
                return;
            }

            lock (_debounceLock)
            {
                _pendingValue = FormatCurveData(values);

                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(DebounceCallback, null, DEBOUNCE_MS, Timeout.Infinite);
                }
                else
                {
                    _debounceTimer.Change(DEBOUNCE_MS, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Sets curve values immediately without debouncing (for initialization/presets)
        /// </summary>
        public async Task<bool> SetCurveValuesImmediateAsync(int[] values)
        {
            if (values == null || values.Length != 10)
            {
                Logger.Warn("Invalid GPD curve values array");
                return false;
            }

            return await RequestAndSyncConfirmedValueAsync(FormatCurveData(values));
        }

        private async void DebounceCallback(object state)
        {
            string valueToSend;
            lock (_debounceLock)
            {
                valueToSend = _pendingValue;
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }

            if (!string.IsNullOrEmpty(valueToSend))
            {
                Logger.Info($"Sending debounced GPD fan curve: {valueToSend}");
                bool confirmed = await RequestAndSyncConfirmedValueAsync(valueToSend);
                if (!confirmed)
                    Logger.Warn("GPD fan curve request was not confirmed by helper");
                if (_requestResultCallback != null && _owner != null)
                {
                    await _owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        _requestResultCallback(confirmed);
                    });
                }
            }
        }

        private async Task<bool> RequestAndSyncConfirmedValueAsync(string requestedValue)
        {
            if (!App.IsConnected) return false;
            await _requestGate.WaitAsync();
            try
            {
                var response = await App.SendMessageAsync(new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)Function.GPDFanCurveData },
                    { nameof(Content), requestedValue }
                });
                if (response == null || response.ContainsKey("Error")) return false;

                response = await App.SendMessageAsync(new ValueSet
                {
                    { nameof(Command), (int)Command.Get },
                    { nameof(Function), (int)Function.GPDFanCurveData }
                });
                if (response == null || !response.TryGetValue(nameof(Content), out object confirmed))
                    return false;

                long updatedTime = response.TryGetValue(nameof(UpdatedTime), out object rawTime)
                    ? Convert.ToInt64(rawTime) : DateTime.Now.Ticks;
                SuppressRemoteSync = true;
                try
                {
                    bool synchronized = SetValue(confirmed, updatedTime);
                    return synchronized && string.Equals(Value, requestedValue, StringComparison.Ordinal);
                }
                finally { SuppressRemoteSync = false; }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to confirm GPD fan curve: {ex.Message}");
                return false;
            }
            finally
            {
                _requestGate.Release();
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (_graphUpdateCallback != null && _owner != null)
            {
                var values = GetCurveValues();
                await _owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    _graphUpdateCallback(values);
                });
            }
        }

        /// <summary>
        /// Formats an array of fan speeds into a comma-separated string
        /// </summary>
        public static string FormatCurveData(int[] values)
        {
            if (values == null || values.Length != 10)
                return FormatCurveData(DefaultCurve);
            return string.Join(",", values);
        }

        /// <summary>
        /// Parses a comma-separated string into an array of fan speeds.
        /// </summary>
        public static int[] ParseCurveData(string data)
        {
            if (string.IsNullOrEmpty(data))
                return (int[])DefaultCurve.Clone();

            try
            {
                var parts = data.Split(',');
                if (parts.Length != 10)
                    return (int[])DefaultCurve.Clone();

                var values = new int[10];
                for (int i = 0; i < 10; i++)
                {
                    int parsed = int.Parse(parts[i].Trim());
                    values[i] = Math.Max(0, Math.Min(100, parsed));
                }
                return values;
            }
            catch
            {
                return (int[])DefaultCurve.Clone();
            }
        }
    }

    /// <summary>
    /// Read-only property for current CPU temperature from the helper.
    /// Used to display temperature indicator on the GPD fan curve graph.
    /// </summary>
    internal class GPDCPUTempProperty : WidgetProperty<int>
    {
        private readonly Page _owner;
        private Action<int> _tempUpdateCallback;

        public GPDCPUTempProperty(Page owner)
            : base(0, null, Function.GPDCPUTemp)
        {
            _owner = owner;
        }

        /// <summary>
        /// Sets a callback to update the temperature indicator on the graph
        /// </summary>
        public void SetTempUpdateCallback(Action<int> callback)
        {
            _tempUpdateCallback = callback;
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            base.NotifyPropertyChanged(propertyName);

            if (_tempUpdateCallback != null && _owner != null)
            {
                var temp = Value;
                await _owner.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    _tempUpdateCallback(temp);
                });
            }
        }
    }

    /// <summary>
    /// Property to tell the helper when the GPD fan curve graph is visible.
    /// The helper only pushes CPU temp updates when this is true.
    /// </summary>
    internal class GPDFanCurveVisibleProperty : WidgetProperty<bool>
    {
        public GPDFanCurveVisibleProperty()
            : base(false, null, Function.GPDFanCurveVisible)
        {
        }

        public void SetVisible(bool visible)
        {
            SetValue(visible);
        }
    }

    /// <summary>
    /// Toggle property for enabling/disabling the GPD software fan curve. Persistence and
    /// ownership live in the helper; the callback only renders helper-confirmed state.
    /// </summary>
    internal class GPDFanCurveEnabledProperty : WidgetProperty<bool>
    {
        private Action<bool> _stateUpdateCallback;

        public GPDFanCurveEnabledProperty()
            : base(false, null, Function.GPDFanCurveEnabled)
        {
        }

        public void SetStateUpdateCallback(Action<bool> callback)
        {
            _stateUpdateCallback = callback;
        }

        public async Task<bool> RequestEnabledAsync(bool enabled)
        {
            if (!App.IsConnected) return false;
            try
            {
                var response = await App.SendMessageAsync(new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)Function.GPDFanCurveEnabled },
                    { nameof(Content), enabled }
                });
                if (response == null || response.ContainsKey("Error")) return false;

                response = await App.SendMessageAsync(new ValueSet
                {
                    { nameof(Command), (int)Command.Get },
                    { nameof(Function), (int)Function.GPDFanCurveEnabled }
                });
                if (response == null || !response.TryGetValue(nameof(Content), out object confirmed))
                    return false;

                long updatedTime = response.TryGetValue(nameof(UpdatedTime), out object rawTime)
                    ? Convert.ToInt64(rawTime) : DateTime.Now.Ticks;
                SuppressRemoteSync = true;
                try
                {
                    bool synchronized = SetValue(confirmed, updatedTime);
                    return synchronized && Value == enabled;
                }
                finally { SuppressRemoteSync = false; }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to confirm GPD fan curve enabled state: {ex.Message}");
                return false;
            }
        }

        protected override void OnValueSyncedFromHelper()
        {
            _stateUpdateCallback?.Invoke(Value);
        }
    }
}
