using System.Windows.Controls;
using OceanyaClient.Components;

namespace OceanyaClient.Components.Panels
{
    /// <summary>
    /// Hosts the IC chat log together with its background art, so the panel's bounds are the whole
    /// visual and the log stretches with it.
    /// </summary>
    /// <remarks>
    /// The log used to be placed next to a separate, taller backdrop rectangle, which drew outside the
    /// panel and did not react to resizing. See <c>Documentation/OceanyaThemeSystem.md</c>.
    /// </remarks>
    public partial class IcLogPanel : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IcLogPanel"/> class.
        /// </summary>
        public IcLogPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets the hosted IC log control.
        /// </summary>
        public ICLog LogControl => HostedLog;
    }
}
