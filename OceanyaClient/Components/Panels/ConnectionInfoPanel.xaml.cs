using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// Connection status chips shown above the GM main window body: server, area, users, and the
    /// optional status, lock and case manager chips.
    /// </summary>
    /// <remarks>
    /// Self-contained panel for the Oceanya theme system: the host passes values in through
    /// <see cref="SetConnectionInfo"/> and never reaches into the chip elements. See
    /// <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class ConnectionInfoPanel : UserControl
    {
        /// <summary>
        /// Area status value that means "nothing interesting", matching AO2's default.
        /// </summary>
        private const string DefaultStatus = "IDLE";

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionInfoPanel"/> class.
        /// </summary>
        public ConnectionInfoPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Updates every chip from the current connection and area state.
        /// </summary>
        /// <param name="server">Server display name, or a disconnected placeholder.</param>
        /// <param name="area">Current area name.</param>
        /// <param name="users">User count, or a negative value when unknown.</param>
        /// <param name="status">Area status; the chip hides while it is the default IDLE.</param>
        /// <param name="lockState">Area lock state; the chip hides while the area is FREE or OPEN.</param>
        /// <param name="caseManager">Case manager name; the chip hides while it is FREE.</param>
        public void SetConnectionInfo(
            string server,
            string area,
            int users,
            string status,
            string lockState,
            string caseManager)
        {
            txtSelectedServerInfo.Text = server;
            txtSelectedAreaInfo.Text = area;
            txtSelectedAreaUsersInfo.Text = users >= 0 ? users.ToString() : "-";
            txtSelectedServerInfo.ToolTip = server;
            txtSelectedAreaInfo.ToolTip = area;
            txtSelectedAreaUsersInfo.ToolTip = users >= 0 ? $"{users} users" : "Unknown users";

            // STATUS chip — hidden when IDLE (the boring default state)
            bool showStatus = !string.Equals(status, DefaultStatus, StringComparison.OrdinalIgnoreCase);
            if (showStatus)
            {
                txtSelectedAreaStatusInfo.Text = status;
                StatusChip.Background = GetStatusChipBrush(status);
                StatusChip.Visibility = Visibility.Visible;
                txtSelectedAreaStatusInfo.ToolTip = $"Area status: {status}";
            }
            else
            {
                StatusChip.Visibility = Visibility.Collapsed;
            }

            // LOCK chip — hidden when FREE (normal unlocked state)
            bool showLock = !string.Equals(lockState, "FREE", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(lockState, "OPEN", StringComparison.OrdinalIgnoreCase);
            if (showLock)
            {
                txtSelectedAreaLockInfo.Text = lockState;
                LockChip.Visibility = Visibility.Visible;
                txtSelectedAreaLockInfo.ToolTip = $"Lock: {lockState}";
            }
            else
            {
                LockChip.Visibility = Visibility.Collapsed;
            }

            // CM chip — hidden when FREE (no active case manager)
            bool showCm = !string.Equals(caseManager, "FREE", StringComparison.OrdinalIgnoreCase);
            if (showCm)
            {
                txtSelectedAreaMetaInfo.Text = caseManager;
                CmChip.Visibility = Visibility.Visible;
                txtSelectedAreaMetaInfo.ToolTip = $"Case manager: {caseManager}";
            }
            else
            {
                CmChip.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Resolves the background brush that tints the status chip for a given area status.
        /// </summary>
        /// <param name="status">Area status text.</param>
        /// <returns>The chip background brush.</returns>
        public static Brush GetStatusChipBrush(string status)
        {
            if (string.Equals(status, "LOOKING-FOR-PLAYERS", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x12, 0x23, 0x18));
            if (string.Equals(status, "CASING", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x1E, 0x1A, 0x0E));
            if (string.Equals(status, "RECESS", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x2A));
            if (string.Equals(status, "RP", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x22, 0x14, 0x28));
            if (string.Equals(status, "GAMING", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x10, 0x24, 0x24));
            return new SolidColorBrush(Color.FromRgb(0x18, 0x15, 0x20));
        }
    }
}
