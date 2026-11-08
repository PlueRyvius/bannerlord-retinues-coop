using System;
using System.Collections.Generic;

namespace Retinues.Configuration
{
    /// <summary>
    /// Option values that win over the player's own settings for the current session only, keyed by option key.
    /// Empty in single player. A multiplayer integration fills it (e.g. with the host's settings) and clears it
    /// when the session ends; nothing here is saved.
    /// </summary>
    public static class SessionOverrides
    {
        private static readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

        public static bool TryGet(string key, out object value)
        {
            lock (_values)
            {
                if (_values.Count == 0 || key == null)
                {
                    value = null;
                    return false;
                }
                return _values.TryGetValue(key, out value);
            }
        }

        public static void Set(string key, object value)
        {
            lock (_values)
                _values[key] = value;
        }

        public static void Clear()
        {
            lock (_values)
                _values.Clear();
        }

        public static bool Any
        {
            get
            {
                lock (_values)
                    return _values.Count > 0;
            }
        }
    }
}
