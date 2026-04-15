using System.Windows;

namespace UnitTests
{
    internal static class WpfTestApplicationContext
    {
        public static Application EnsureCreated()
        {
            Application application = Application.Current ?? new Application();
            application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            return application;
        }
    }
}
