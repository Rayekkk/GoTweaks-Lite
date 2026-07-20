using NLog;
using System;

namespace XboxGamingBarHelper.AMD
{
    internal static class AMDUtilities
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        internal static bool GetBoolValue(GetBool func)
        {
            TryGetBoolValue(func, out bool value);
            return value;
        }

        // The Try* variants distinguish a real read from a failed one (the plain GetBoolValue/
        // GetIntValue/GetIntRangeValue wrappers above just fall back to false/0/(0,0) on failure).
        // They were originally added for the external-change-detection listener; that listener was
        // removed (GoTweaks→AMD is now the only sync direction), so today the plain wrappers - which
        // delegate here - are the only live consumers. Kept because the wrappers depend on them.
        internal static bool TryGetBoolValue(GetBool func, out bool value)
        {
            value = false;
            var boolPointer = ADLX.new_boolP();
            try
            {
                var getValueResult = func(boolPointer);
                if (getValueResult != ADLX_RESULT.ADLX_OK)
                {
                    Logger.Error($"Failed to get AMD bool value. ADLX_RESULT: {getValueResult}");
                    return false;
                }
                value = ADLX.boolP_value(boolPointer);
                return true;
            }
            finally
            {
                ADLX.delete_boolP(boolPointer);
            }
        }

        internal static Tuple<int, int> GetIntRangeValue(GetIntRange func)
        {
            TryGetIntRangeValue(func, out Tuple<int, int> value);
            return value;
        }

        // See TryGetBoolValue for why this exists alongside the plain GetIntRangeValue above.
        internal static bool TryGetIntRangeValue(GetIntRange func, out Tuple<int, int> value)
        {
            value = new Tuple<int, int>(0, 0);
            var intRangePointer = ADLX.new_intRangeP();
            try
            {
                var getValueResult = func(intRangePointer);
                if (getValueResult != ADLX_RESULT.ADLX_OK)
                {
                    Logger.Error($"Failed to get AMD int range value. ADLX_RESULT: {getValueResult}");
                    return false;
                }
                var intRangeValue = ADLX.intRangeP_value(intRangePointer);
                value = new Tuple<int, int>(intRangeValue.minValue, intRangeValue.maxValue);
                return true;
            }
            finally
            {
                ADLX.delete_intRangeP(intRangePointer);
            }
        }

        internal static int GetIntValue(GetInt func)
        {
            TryGetIntValue(func, out int value);
            return value;
        }

        // See TryGetBoolValue for why this exists alongside the plain GetIntValue above.
        internal static bool TryGetIntValue(GetInt func, out int value)
        {
            value = 0;
            var intPointer = ADLX.new_intP();
            try
            {
                var getValueResult = func(intPointer);
                if (getValueResult != ADLX_RESULT.ADLX_OK)
                {
                    Logger.Error($"Failed to get AMD int value. ADLX_RESULT: {getValueResult}");
                    return false;
                }
                value = ADLX.intP_value(intPointer);
                return true;
            }
            finally
            {
                ADLX.delete_intP(intPointer);
            }
        }
    }
}
