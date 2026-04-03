using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class CountdownTimer
{
    private readonly Stopwatch stopwatch;
    private TimeSpan duration;
    private CancellationTokenSource? cancellationTokenSource;
    private readonly object lockObj = new object();

    public event Action? TimerElapsed;

    public CountdownTimer(TimeSpan duration)
    {
        this.duration = duration;
        stopwatch = new Stopwatch();
    }

    public void Start()
    {
        CancellationTokenSource? localCancellationTokenSource = null;
        TimeSpan localDuration = TimeSpan.Zero;

        lock (lockObj)
        {
            if (stopwatch.IsRunning)
            {
                return;
            }

            RestartTimerLocked(out localCancellationTokenSource, out localDuration);
        }

        StartTimerTask(localCancellationTokenSource!, localDuration);
    }

    public void Stop()
    {
        CancellationTokenSource? previousCancellationTokenSource;

        lock (lockObj)
        {
            previousCancellationTokenSource = cancellationTokenSource;
            cancellationTokenSource = null;
            stopwatch.Stop();
        }

        CancelTimer(previousCancellationTokenSource);
    }

    public void Reset(TimeSpan newDuration)
    {
        CancellationTokenSource? previousCancellationTokenSource;
        CancellationTokenSource? localCancellationTokenSource;
        TimeSpan localDuration;

        lock (lockObj)
        {
            previousCancellationTokenSource = cancellationTokenSource;
            cancellationTokenSource = null;
            stopwatch.Stop();
            duration = newDuration;
            RestartTimerLocked(out localCancellationTokenSource, out localDuration);
        }

        CancelTimer(previousCancellationTokenSource);
        StartTimerTask(localCancellationTokenSource!, localDuration);
    }

    private void RestartTimerLocked(
        out CancellationTokenSource localCancellationTokenSource,
        out TimeSpan localDuration)
    {
        localCancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource = localCancellationTokenSource;
        stopwatch.Restart();
        localDuration = duration;
    }

    private void StartTimerTask(CancellationTokenSource localCancellationTokenSource, TimeSpan localDuration)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(localDuration, localCancellationTokenSource.Token);

                bool shouldInvoke = false;
                lock (lockObj)
                {
                    if (ReferenceEquals(cancellationTokenSource, localCancellationTokenSource)
                        && !localCancellationTokenSource.IsCancellationRequested)
                    {
                        cancellationTokenSource = null;
                        stopwatch.Stop();
                        shouldInvoke = true;
                    }
                }

                if (shouldInvoke)
                {
                    TimerElapsed?.Invoke();
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                localCancellationTokenSource.Dispose();
            }
        }, localCancellationTokenSource.Token);
    }

    private static void CancelTimer(CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource == null)
        {
            return;
        }

        cancellationTokenSource.Cancel();
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
