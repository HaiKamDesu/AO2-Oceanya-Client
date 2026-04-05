using System;

namespace OceanyaClient.Features.Startup
{
    /// <summary>
    /// Gates the developer-only AO2 AI Bot startup mode.
    /// </summary>
    public static class AO2AiBotDeveloperAccess
    {
        /// <summary>
        /// Gets a value indicating whether the current runtime should expose the AI startup mode.
        /// </summary>
        public static bool IsStartupModeVisible
        {
            get
            {
#if DEBUG
                string userName = Environment.UserName?.Trim() ?? string.Empty;
                return string.Equals(userName, "Usuario", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(userName, "usuario", StringComparison.OrdinalIgnoreCase);
#else
                return false;
#endif
            }
        }
    }
}
