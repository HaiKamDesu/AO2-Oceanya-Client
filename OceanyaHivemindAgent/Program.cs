using Common;
using OceanyaClient.Features.FileHivemind;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace OceanyaHivemindAgent
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
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
        private readonly Task agentTask;
        private bool exitRequested;

        public FileHivemindAgentApplicationContext(string[] args)
        {
            cancellationTokenSource = new CancellationTokenSource();
            trayIconController = new FileHivemindTrayIconController(RequestExit);
            agent = new FileHivemindBackgroundSyncAgent(backgroundNotifier: trayIconController);
            completionTimer = new Forms.Timer
            {
                Interval = 500,
                Enabled = true
            };
            completionTimer.Tick += CompletionTimer_Tick;
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
            cancellationTokenSource.Cancel();
        }

        protected override void ExitThreadCore()
        {
            completionTimer.Stop();
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
            agent.Dispose();
            cancellationTokenSource.Dispose();
            base.ExitThreadCore();
        }
    }
}
