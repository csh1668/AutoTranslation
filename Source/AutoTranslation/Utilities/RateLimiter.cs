using System;
using System.Diagnostics;
using System.Threading;

namespace AutoTranslation.Utilities
{

    public sealed class RateLimiter
    {
        private readonly object _lock = new object();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly long _intervalTicks;
        private long _nextSlotTicks;
        private long _pausedUntilTicks;

        public RateLimiter(int requestsPerMinute)
        {
            if (requestsPerMinute <= 0) throw new ArgumentOutOfRangeException(nameof(requestsPerMinute));
            _intervalTicks = TimeSpan.TicksPerMinute / requestsPerMinute;
        }

        public void Wait()
        {
            while (true)
            {
                long slot;
                lock (_lock)
                {
                    var now = _clock.Elapsed.Ticks;
                    if (_nextSlotTicks < now) _nextSlotTicks = now;
                    slot = _nextSlotTicks;
                    _nextSlotTicks += _intervalTicks;
                }

                long remaining;
                while ((remaining = slot - _clock.Elapsed.Ticks) > 0)
                    Thread.Sleep(TimeSpan.FromTicks(remaining));

                lock (_lock)
                {
                    if (_clock.Elapsed.Ticks >= _pausedUntilTicks) return;
                }
            }
        }

        public bool PauseFor(TimeSpan duration)
        {
            lock (_lock)
            {
                var now = _clock.Elapsed.Ticks;
                var wasPaused = now < _pausedUntilTicks;
                _pausedUntilTicks = Math.Max(_pausedUntilTicks, now + duration.Ticks);
                _nextSlotTicks = Math.Max(_nextSlotTicks, _pausedUntilTicks);
                return !wasPaused;
            }
        }
    }
}
