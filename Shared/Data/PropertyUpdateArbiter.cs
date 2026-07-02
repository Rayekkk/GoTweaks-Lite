using System;

namespace Shared.Data
{
    /// <summary>
    /// Pure, value-agnostic timestamp arbitration for property updates — the rules that
    /// decide whether an incoming update wins over the currently stored one based purely
    /// on their timestamps. Extracted from <see cref="GenericProperty{ValueType}"/> so the
    /// rules can be unit-tested in isolation (a concrete property pulls in WinRT
    /// <c>ValueSet</c> via its abstract members, which can't be loaded in a plain test host).
    ///
    /// This intentionally contains NO ValueSet / WinRT / value-type-specific logic — the
    /// caller still owns value equality (scalar vs list) and the actual store/notify.
    /// </summary>
    public static class PropertyUpdateArbiter
    {
        /// <summary>
        /// Applies the staleness + "no info" (UpdatedTime==0) rules.
        /// Returns <c>false</c> when the incoming update should be rejected; otherwise
        /// <c>true</c> with <paramref name="effectiveUpdatedTime"/> set to the timestamp the
        /// property should record.
        ///
        /// Rules (unchanged from the original GenericProperty.SetValue):
        /// <list type="bullet">
        /// <item>incoming==0 means "no info". If a real prior timestamp already exists
        /// (<paramref name="lastUpdatedTime"/> &gt; 0) the update is rejected — this stops a
        /// racing zero-stamped BatchGet snapshot from clobbering an authoritative value
        /// (issue #79). On a fresh property (lastUpdatedTime==0) it is coerced to "now".</item>
        /// <item>An update strictly older than the stored timestamp is rejected as stale.</item>
        /// </list>
        /// </summary>
        public static bool TryResolveTimestamp(long lastUpdatedTime, long incomingUpdatedTime, out long effectiveUpdatedTime)
        {
            effectiveUpdatedTime = incomingUpdatedTime;

            if (incomingUpdatedTime == 0)
            {
                if (lastUpdatedTime > 0)
                {
                    return false;
                }
                effectiveUpdatedTime = DateTime.Now.Ticks;
            }

            if (effectiveUpdatedTime < lastUpdatedTime)
            {
                return false;
            }

            return true;
        }
    }
}
