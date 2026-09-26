using System;
using System.Diagnostics;
using System.Threading;

namespace Ryujinx.Cpu
{
    public class TickSource : ITickSource
    {
        private sealed class TimeScaleState
        {
            public long HostOriginTicks { get; }
            public double GuestOriginTicks { get; }
            public double Scale { get; }

            public TimeScaleState(long hostOriginTicks, double guestOriginTicks, double scale)
            {
                HostOriginTicks = hostOriginTicks;
                GuestOriginTicks = guestOriginTicks;
                Scale = scale;
            }
        }

        private static Stopwatch _tickCounter;
        private static double _hostTickFreq;
        private static readonly object _timeScaleLock = new();
        private static TimeScaleState _timeScaleState = new(0, 0, 1.0);

        /// <inheritdoc/>
        public ulong Frequency { get; }

        /// <inheritdoc/>
        public ulong Counter => (ulong)(ElapsedSeconds * Frequency);

        /// <inheritdoc/>
        public TimeSpan ElapsedTime => TimeSpan.FromSeconds(ElapsedSeconds);

        /// <inheritdoc/>
        public double ElapsedSeconds => GetScaledElapsedTicks() * _hostTickFreq;

        public static double TimeScale => Volatile.Read(ref _timeScaleState).Scale;

        public TickSource(ulong frequency)
        {
            Frequency = frequency;
            _hostTickFreq = 1.0 / Stopwatch.Frequency;

            _tickCounter = new Stopwatch();
            _tickCounter.Start();

            Volatile.Write(ref _timeScaleState, new TimeScaleState(0, 0, 1.0));
        }

        private static double GetScaledElapsedTicks()
        {
            Stopwatch counter = _tickCounter;
            if (counter == null)
            {
                return 0;
            }

            TimeScaleState state = Volatile.Read(ref _timeScaleState);
            long hostTicks = counter.ElapsedTicks;

            return state.GuestOriginTicks + ((hostTicks - state.HostOriginTicks) * state.Scale);
        }

        public static void SetTimeScale(double scale)
        {
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }

            lock (_timeScaleLock)
            {
                Stopwatch counter = _tickCounter;
                if (counter == null)
                {
                    Volatile.Write(ref _timeScaleState, new TimeScaleState(0, 0, scale));
                    return;
                }

                TimeScaleState oldState = Volatile.Read(ref _timeScaleState);
                long hostTicks = counter.ElapsedTicks;
                double guestTicks = oldState.GuestOriginTicks +
                                    ((hostTicks - oldState.HostOriginTicks) * oldState.Scale);

                Volatile.Write(
                    ref _timeScaleState,
                    new TimeScaleState(hostTicks, guestTicks, scale));
            }
        }

        /// <inheritdoc/>
        public void Suspend()
        {
            _tickCounter.Stop();
        }

        /// <inheritdoc/>
        public void Resume()
        {
            _tickCounter.Start();
        }
    }
}
