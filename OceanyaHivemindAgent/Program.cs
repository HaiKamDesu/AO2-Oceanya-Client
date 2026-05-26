using Common;
using OceanyaClient;
using OceanyaClient.Features.FileHivemind;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace OceanyaHivemindAgent
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            SaveData snapshot = SaveFile.LoadSnapshotFromDisk();
            if (!FileHivemindBackgroundAgentLauncher.HasEligibleConnections(snapshot.FileHivemind))
            {
                return;
            }

            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);
            using FileHivemindAgentApplicationContext context = new FileHivemindAgentApplicationContext(args);
            Forms.Application.Run(context);
        }
    }

    internal sealed class FileHivemindAgentApplicationContext : Forms.ApplicationContext
    {
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly FileHivemindBackgroundSyncAgent agent;
        private readonly FileHivemindTrayIconController trayIconController;
        private readonly Forms.Timer completionTimer;
        private readonly Forms.Timer stopSignalTimer;
        private readonly EventWaitHandle stopRequestedEvent;
        private readonly EventWaitHandle stoppedEvent;
        private readonly Task agentTask;
        private readonly RegisteredWaitHandle stopSignalRegistration;
        private bool exitRequested;

        public FileHivemindAgentApplicationContext(string[] args)
        {
            cancellationTokenSource = new CancellationTokenSource();
            trayIconController = new FileHivemindTrayIconController(RequestExit);
            agent = new FileHivemindBackgroundSyncAgent(backgroundNotifier: trayIconController);
            stopRequestedEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindBackgroundAgentCommandLine.AgentStopSignalEventName);
            stoppedEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                FileHivemindBackgroundAgentCommandLine.AgentStoppedSignalEventName);
            stopRequestedEvent.Reset();
            stoppedEvent.Reset();
            completionTimer = new Forms.Timer
            {
                Interval = 500,
                Enabled = true
            };
            completionTimer.Tick += CompletionTimer_Tick;
            stopSignalTimer = new Forms.Timer
            {
                Interval = 500,
                Enabled = true
            };
            stopSignalTimer.Tick += StopSignalTimer_Tick;
            stopSignalRegistration = ThreadPool.RegisterWaitForSingleObject(
                stopRequestedEvent,
                (_, _) => RequestExit(),
                null,
                Timeout.InfiniteTimeSpan,
                executeOnlyOnce: false);
            agentTask = Task.Run(() => agent.RunAsync(cancellationTokenSource.Token));
        }

        private void CompletionTimer_Tick(object? sender, EventArgs e)
        {
            if (!agentTask.IsCompleted)
            {
                return;
            }

            completionTimer.Stop();

            if (agentTask.IsFaulted && agentTask.Exception != null)
            {
                CustomConsole.Error("The Oceanyan File Hivemind agent crashed.", agentTask.Exception);
            }

            ExitThread();
        }

        private void RequestExit()
        {
            if (exitRequested)
            {
                return;
            }

            exitRequested = true;
            CustomConsole.Info("The Oceanyan File Hivemind agent received a stop request.");
            cancellationTokenSource.Cancel();
        }

        private void StopSignalTimer_Tick(object? sender, EventArgs e)
        {
            if (!stopRequestedEvent.WaitOne(0))
            {
                return;
            }

            RequestExit();
        }

        protected override void ExitThreadCore()
        {
            CustomConsole.Info("The Oceanyan File Hivemind agent is shutting down.");
            completionTimer.Stop();
            stopSignalTimer.Stop();
            stopSignalRegistration.Unregister(null);
            cancellationTokenSource.Cancel();

            try
            {
                if (!agentTask.IsCompleted)
                {
                    agentTask.Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch (AggregateException ex)
            {
                CustomConsole.Error("The Oceanyan File Hivemind agent failed while shutting down.", ex.Flatten());
            }

            trayIconController.Dispose();
            completionTimer.Dispose();
            stopSignalTimer.Dispose();
            stoppedEvent.Set();
            stoppedEvent.Dispose();
            stopRequestedEvent.Dispose();
            agent.Dispose();
            cancellationTokenSource.Dispose();
            base.ExitThreadCore();
        }
    }
}
