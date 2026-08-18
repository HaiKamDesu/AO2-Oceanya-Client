using System.Windows.Controls;
using OceanyaClient.Components;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// Hosts the OOC chat log together with its background fill, so the panel's bounds are the whole
    /// visual and the log stretches with it.
    /// </summary>
    public partial class OocLogPanel : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OocLogPanel"/> class.
        /// </summary>
        public OocLogPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets the hosted OOC log control.
        /// </summary>
        public OOCLog LogControl => HostedLog;
    }
}
