using AOBot_Testing.Agents;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Hosts the AO2 viewport surface inside the shared Oceanya window chrome.
    /// </summary>
    public partial class AO2ViewportWindowContent : OceanyaWindowContentControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AO2ViewportWindowContent"/> class.
        /// </summary>
        public AO2ViewportWindowContent()
        {
            InitializeComponent();
        }

        /// <inheritdoc/>
        public override string HeaderText => "Viewport";

        /// <inheritdoc/>
        public override bool IsUserResizeEnabled => true;

        /// <inheritdoc/>
        public override bool IsUserMoveEnabled => true;

        /// <inheritdoc/>
        public override bool IsCloseButtonVisible => true;

        /// <summary>
        /// Attaches the hosted viewport to a client profile.
        /// </summary>
        public void AttachClient(AOClient? client)
        {
            ViewportControl.AttachClient(client);
        }

        /// <summary>
        /// Attaches the hosted viewport to a selected profile and the client that receives server IC echoes.
        /// </summary>
        public void AttachClient(AOClient? client, AOClient? incomingMessageClient)
        {
            ViewportControl.AttachClient(client, incomingMessageClient);
        }
    }
}
