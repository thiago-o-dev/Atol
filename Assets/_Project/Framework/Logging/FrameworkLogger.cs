using UnityEngine;

namespace Assets._Project.Framework.Logging
{
    /// <summary>
    /// Lightweight logger owned by a MonoBehaviour.
    ///
    /// The logger itself does not need to exist as a GameObject/component.
    /// It simply uses the owner to obtain contextual information such as
    /// the GameObject name and to provide Unity's Console object context.
    /// </summary>
    public sealed class FrameworkLogger
    {
        // Global switch for turning framework logging on/off.
        public static bool GlobalDebugEnabled { get; set; } = true;

        private readonly MonoBehaviour _context;

        private readonly bool _showLogs;
        private readonly string _prefix;
        private readonly string _hexColor;

        /// <summary>
        /// Creates a logger associated with a specific MonoBehaviour instance.
        /// </summary>
        /// <param name="owner">The component that owns this logger.</param>
        /// <param name="showLogs">Whether this particular logger should output logs.</param>
        /// <param name="prefix">
        /// Optional prefix. If omitted, the owner's GameObject name is used.
        /// </param>
        /// <param name="prefixColor">Color used for the prefix in the Unity Console.</param>
        public FrameworkLogger(
            MonoBehaviour owner,
            bool showLogs = true,
            string prefix = null,
            Color? prefixColor = null)
        {
            _context = owner;

            _showLogs = showLogs;

            // Use the explicitly supplied prefix when available.
            // Otherwise, fall back to the owner's GameObject name so logs
            // automatically identify where they came from.
            _prefix = string.IsNullOrEmpty(prefix)
                ? owner.gameObject.name
                : prefix;

            var color = prefixColor ?? Color.white;

            _hexColor = "#" + ColorUtility.ToHtmlStringRGBA(color);
        }
        public void Debug(object message)
        {
            if (!GlobalDebugEnabled || !_showLogs)
                return;

            UnityEngine.Debug.Log(
                Format(message),
                _context);
        }

        public void Log(object message)
        {
            if (!_showLogs)
                return;

            UnityEngine.Debug.Log(
                Format(message),
                _context);
        }

        public void Warning(object message)
        {
            if (!_showLogs)
                return;

            UnityEngine.Debug.LogWarning(
                Format(message),
                _context);
        }

        public void Error(object message)
        {
            if (!_showLogs)
                return;

            UnityEngine.Debug.LogError(
                Format(message),
                _context);
        }

        private string Format(object message)
        {
            return $"<color={_hexColor}>[{_prefix}]</color>: {message}";
        }
    }
}