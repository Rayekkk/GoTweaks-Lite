using System;
using Shared.Data;
using Shared.Enums;
using Shared.IPC;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Collections;

namespace XboxGamingBar.Data
{
    internal class WidgetProperty<ValueType> : GenericProperty<ValueType>
    {
        private long setRequestSequence;
        private ValueType confirmedValue;

        public WidgetProperty(ValueType inValue, IProperty inParentProperty, Function inFunction) : base(inValue, inParentProperty, inFunction)
        {
            confirmedValue = inValue;
        }

        private void RestoreLastConfirmedValue()
        {
            SuppressRemoteSync = true;
            try { ForceSetValue(confirmedValue); }
            finally { SuppressRemoteSync = false; }
        }

        /// <summary>
        /// Override Sync to use the unified App.SendMessageAsync for Named Pipe communication.
        /// </summary>
        public override async Task Sync()
        {
            if (!App.IsConnected)
            {
                Logger.Warn($"Can't sync {function} - no connection.");
                return;
            }

            var request = new ValueSet
            {
                { nameof(Command), (int)Command.Get },
                { nameof(Function), (int)function },
            };

            var response = await App.SendMessageAsync(request);
            if (response != null)
            {
                if (response.TryGetValue(nameof(Content), out object responseValue))
                {
                    if (response.TryGetValue(nameof(UpdatedTime), out object updatedTimeValue))
                    {
                        var updatedTime = Convert.ToInt64(updatedTimeValue);
                        SuppressRemoteSync = true;
                        bool synchronized;
                        try
                        {
                            synchronized = SetValue(responseValue, updatedTime);
                            confirmedValue = Value;
                        }
                        finally { SuppressRemoteSync = false; }
                        if (synchronized)
                        {
                            Logger.Info($"Sync {function} value {responseValue} successfully.");
                        }
                        else
                        {
                            Logger.Warn($"Got {function} value {responseValue} but can't sync.");
                        }
                    }
                    else
                    {
                        Logger.Warn($"Can't get updated time when trying to sync property {function}.");
                    }
                }
                else
                {
                    Logger.Warn($"Got empty response when trying to sync property {function}.");
                }
            }
            else
            {
                Logger.Warn($"Got no response when trying to sync property {function}.");
            }
        }

        protected override async Task<object> SendMessageAsync(ValueSet request)
        {
            if (!App.IsConnected)
            {
                Logger.Debug($"Widget property {function} - not connected.");
                return null;
            }

            return await App.SendMessageAsync(request);
        }

        /// <summary>
        /// Override NotifyPropertyChanged to use App.SendMessageAsync for Named Pipe communication.
        /// We call InvokePropertyChanged directly to fire the INotifyPropertyChanged event.
        /// </summary>
        /// <summary>
        /// Called when a value arrives from the helper (SuppressRemoteSync path), after the
        /// INotifyPropertyChanged event fires and before the (skipped) remote echo. Default is
        /// a no-op — auto-bound controls already reflect the value via data binding. Overridden
        /// by properties whose UI is composite and must be rebuilt from the pushed value.
        /// </summary>
        protected virtual void OnValueSyncedFromHelper() { }

        protected virtual Task OnSetRequestPendingChanged(bool pending)
        {
            return Task.CompletedTask;
        }

        protected virtual Task OnSetRequestFailed(string reason)
        {
            return Task.CompletedTask;
        }

        private bool ApplyConfirmedResponse(ValueSet response, long requestTimestamp, long sequence, out string failureReason)
        {
            failureReason = null;
            if (sequence != Interlocked.Read(ref setRequestSequence))
                return false;

            if (!response.TryGetValue(PropertySetContract.OutcomeField, out object outcome)
                || !PropertySetContract.IsKnownOutcome(outcome))
            {
                // Compatibility with a pre-2.0 helper. It cannot provide effective-state
                // confirmation, so keep the old behavior until the helper upgrade completes.
                return true;
            }

            // A helper push newer than this request is already authoritative. Do not let the
            // delayed response overwrite it.
            if (UpdatedTime > requestTimestamp && PropertySetContract.IsApplied(outcome))
                return true;

            if (response.TryGetValue(nameof(Content), out object effectiveValue))
            {
                SuppressRemoteSync = true;
                try
                {
                    // This response is correlated to the newest local request, so it supersedes
                    // the optimistic local timestamp even when the helper rejected the request
                    // and its last effective revision is older.
                    SetValue(effectiveValue, DateTime.Now.Ticks);
                    confirmedValue = Value;
                }
                finally
                {
                    SuppressRemoteSync = false;
                }
            }

            if (!PropertySetContract.IsApplied(outcome))
            {
                failureReason = response.TryGetValue(PropertySetContract.ReasonField, out object reason)
                    ? reason?.ToString()
                    : "The helper rejected the requested setting.";
                return false;
            }

            return true;
        }

        private async Task<bool> ReconcileConfirmedValueAfterFailure(long sequence)
        {
            if (!App.IsConnected || sequence != Interlocked.Read(ref setRequestSequence)) return false;
            try
            {
                var response = await App.SendMessageAsync(new ValueSet
                {
                    { nameof(Command), (int)Command.Get },
                    { nameof(Function), (int)function }
                });
                if (response == null || sequence != Interlocked.Read(ref setRequestSequence)
                    || !response.TryGetValue(nameof(Content), out object effectiveValue))
                    return false;

                SuppressRemoteSync = true;
                try
                {
                    SetValue(effectiveValue, DateTime.Now.Ticks);
                    confirmedValue = Value;
                }
                finally { SuppressRemoteSync = false; }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to reconcile confirmed {function} state: {ex.Message}");
                return false;
            }
        }

        protected override async void NotifyPropertyChanged(string propertyName = "")
        {
            // Call InvokePropertyChanged directly to trigger INotifyPropertyChanged events
            InvokePropertyChanged(propertyName);

            // Skip sending to remote if suppressed (e.g., during batch sync)
            if (SuppressRemoteSync)
            {
                confirmedValue = Value;
                // SuppressRemoteSync is set on both sync paths (batch sync in WidgetProperties
                // and an individual Set push in FunctionalProperties.HandlePipeMessage), so this
                // is exactly the "value arrived from the helper" case. Give subclasses a chance
                // to reflect it into composite UI that isn't auto-bound to a single control
                // (Legion button remaps rebuild their whole per-button UI from the pushed JSON).
                OnValueSyncedFromHelper();
                return;
            }

            if (!App.IsConnected)
            {
                Logger.Debug($"Widget property {function} - skipping remote sync (not connected).");
                RestoreLastConfirmedValue();
                await OnSetRequestFailed("The helper is disconnected; the setting was not applied.");
                return;
            }

            long sequence = Interlocked.Increment(ref setRequestSequence);
            long requestTimestamp = UpdatedTime;
            await OnSetRequestPendingChanged(true);
            try
            {
                var request = new ValueSet
                {
                    { nameof(Command), (int)Command.Set },
                    { nameof(Function), (int)function },
                };
                request = AddValueSetContent(request);

                var response = await App.SendMessageAsync(request);
                if (sequence != Interlocked.Read(ref setRequestSequence))
                    return;

                if (response != null)
                {
                    if (!ApplyConfirmedResponse(response, requestTimestamp, sequence, out string failureReason))
                    {
                        Logger.Warn($"Helper did not apply {function}: {failureReason}");
                        await OnSetRequestFailed(failureReason);
                    }
                    else if (response.TryGetValue("Error", out object errorValue))
                    {
                        Logger.Warn($"Error notifying property {function}: {errorValue}");
                        if (!await ReconcileConfirmedValueAfterFailure(sequence))
                            RestoreLastConfirmedValue();
                        await OnSetRequestFailed(errorValue?.ToString());
                    }
                    else
                    {
                        Logger.Debug($"Helper confirmed property {function}.");
                    }
                }
                else
                {
                    Logger.Warn($"Got no response when notifying property {function}.");
                    if (!await ReconcileConfirmedValueAfterFailure(sequence))
                        RestoreLastConfirmedValue();
                    await OnSetRequestFailed("The helper did not respond; restored its last confirmed value.");
                }
            }
            catch (Exception ex)
            {
                if (sequence != Interlocked.Read(ref setRequestSequence))
                    return;

                Logger.Warn($"NotifyPropertyChanged failed for {function}: {ex.Message}");
                if (!await ReconcileConfirmedValueAfterFailure(sequence))
                    RestoreLastConfirmedValue();
                await OnSetRequestFailed(ex.Message);
            }
            finally
            {
                if (sequence == Interlocked.Read(ref setRequestSequence))
                    await OnSetRequestPendingChanged(false);
            }
        }
    }
}
