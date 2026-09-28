#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlashEditor.Tests.LiveEdit
{
    /// <summary>
    ///     A clock that moves only when a test advances it, firing each timer that falls due on
    ///     the test's own thread and in due order.
    /// </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> timers = new List<ManualTimer>();
        private DateTimeOffset now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            DateTimeOffset target = now + by;
            while (true)
            {
                ManualTimer? next = timers
                    .Where(t => t.DueAt.HasValue && t.DueAt.Value <= target)
                    .OrderBy(t => t.DueAt!.Value)
                    .FirstOrDefault();
                if (next == null) break;
                now = next.DueAt!.Value;
                next.Fire();
            }
            now = target;
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider owner;
            private readonly TimerCallback callback;
            private readonly object? state;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
            {
                this.owner = owner;
                this.callback = callback;
                this.state = state;
            }

            public DateTimeOffset? DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                this.period = period;
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.now + dueTime;
                return true;
            }

            public void Fire()
            {
                DueAt = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : owner.now + period;
                callback(state);
            }

            public void Dispose() => DueAt = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
