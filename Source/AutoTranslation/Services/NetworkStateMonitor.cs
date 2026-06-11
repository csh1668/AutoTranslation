using System;
using Verse;

namespace AutoTranslation.Services
{
    /// <summary>
    /// Circuit breaker for translation network requests.
    /// Opens after consecutive connection-level failures so the queue stops
    /// hammering an unreachable network; lets a single probe item through
    /// periodically to detect recovery. Judges reachability ONLY by actual
    /// translator request outcomes, so local endpoints (LM Studio) keep
    /// working with no internet.
    /// </summary>
    public static class NetworkStateMonitor
    {
        private const int FAILURE_THRESHOLD = 5;
        private const int PROBE_INTERVAL_MS = 60000;
        private const int PROBE_STUCK_TIMEOUT_MS = 300000; // safety: probe never reported back

        private static readonly object _lock = new object();
        private static int _consecutiveFailures;
        private static bool _isOpen;
        private static DateTime _lastFailureTime = DateTime.MinValue;
        private static bool _probeInFlight;
        private static DateTime _probeStartTime = DateTime.MinValue;

        public static bool IsOpen
        {
            get { lock (_lock) return _isOpen; }
        }

        /// <summary>Called for every connection-level request failure.</summary>
        public static void ReportFailure()
        {
            lock (_lock)
            {
                _probeInFlight = false;
                _lastFailureTime = DateTime.UtcNow;
                _consecutiveFailures++;
                if (!_isOpen && _consecutiveFailures >= FAILURE_THRESHOLD)
                {
                    _isOpen = true;
                    Log.Warning($"{AutoTranslation.LogPrefix} Network unreachable after {_consecutiveFailures} consecutive failures. Pausing translation queue.");
                }
            }
        }

        /// <summary>
        /// Called whenever the endpoint was reachable - including server error
        /// responses (401/429 etc.), which prove connectivity.
        /// </summary>
        public static void ReportSuccess()
        {
            lock (_lock)
            {
                _consecutiveFailures = 0;
                _probeInFlight = false;
                if (_isOpen)
                {
                    _isOpen = false;
                    Log.Message($"{AutoTranslation.LogPrefix} Network recovered. Resuming translation queue.");
                }
            }
        }

        /// <summary>
        /// When the circuit is open, grants a single probe slot every PROBE_INTERVAL_MS.
        /// Returns true if the caller may send one real request as a probe.
        /// </summary>
        public static bool TryEnterProbe()
        {
            lock (_lock)
            {
                if (!_isOpen) return false;
                if (_probeInFlight)
                {
                    // Safety: a probe that never made a network call would leave the flag stuck
                    if ((DateTime.UtcNow - _probeStartTime).TotalMilliseconds < PROBE_STUCK_TIMEOUT_MS) return false;
                    _probeInFlight = false;
                }
                if ((DateTime.UtcNow - _lastFailureTime).TotalMilliseconds < PROBE_INTERVAL_MS) return false;
                _probeInFlight = true;
                _probeStartTime = DateTime.UtcNow;
                return true;
            }
        }
    }
}
