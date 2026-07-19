using System;

namespace Shared.IPC
{
    /// <summary>
    /// Backwards-compatible metadata attached to property Set responses. Older peers ignore
    /// these fields; newer peers use them to distinguish a requested value from helper-confirmed
    /// effective state.
    /// </summary>
    public static class PropertySetContract
    {
        public const string OutcomeField = "Outcome";
        public const string ReasonField = "Reason";
        public const string RevisionField = "Revision";

        public const string Applied = "Applied";
        public const string Rejected = "Rejected";
        public const string Failed = "Failed";

        public static bool IsKnownOutcome(object value)
        {
            string text = value?.ToString();
            return string.Equals(text, Applied, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, Rejected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, Failed, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsApplied(object value)
        {
            return string.Equals(value?.ToString(), Applied, StringComparison.OrdinalIgnoreCase);
        }
    }
}
