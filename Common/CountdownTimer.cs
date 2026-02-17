using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class CountdownTimer
{
    private Stopwatch stopwatch;
    private TimeSpan duration;
    private CancellationTokenSource? cancellationTokenSource;
    private object lockObj = new object();
    private bool isResetting = false; // NEW: Flag to track resets

    public event Action? TimerElapsed;

    public CountdownTimer(TimeSpan duration)
    {
        this.duration = duration;
        stopwatch = new Stopwatch();
    }

    public void Start()
    {
        lock (lockObj)
        {
            if (stopwatch.IsRunning)
                return; // Avoid restarting an already running timer

            RestartTimer();
        }
    }

    public void Stop()
    {
        lock (lockObj)
        {
            cancellationTokenSource?.Cancel();
            isResetting = true; // NEW: Mark that we are stopping/resetting
            stopwatch.Stop();
        }
    }

    public void Reset(TimeSpan newDuration)
    {
        lock (lockObj)
        {
            Stop(); // Ensure previous timer is canceled
            duration = newDuration;
            isResetting = false; // Reset flag for new countdown
            RestartTimer();
        }
    }

    private void RestartTimer()
    {
        cancellationTokenSource = new CancellationTokenSource();
        stopwatch.Restart();

        Task.Run(async () =>
        {
            try
            {
                while (stopwatch.Elapsed < duration)
                {
                    await Task.Delay(100, cancellationTokenSource.Token);
                }

                lock (lockObj)
                {
                    if (!cancellationTokenSource.Token.IsCancellationRequested && !isResetting)
                    {
                        TimerElapsed?.Invoke();
                        stopwatch.Stop();
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // Task was canceled, do nothing
            }
        }, cancellationTokenSource.Token);
    }

    public TimeSpan GetRemainingTime()
    {
        lock (lockObj)
        {
            var remainingTime = duration - stopwatch.Elapsed;
            return remainingTime < TimeSpan.Zero ? TimeSpan.Zero : remainingTime;
        }
    }

    public bool IsRunning => stopwatch.IsRunning;
}
