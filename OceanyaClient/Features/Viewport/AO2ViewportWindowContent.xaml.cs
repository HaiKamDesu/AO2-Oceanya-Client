using System;
using System.Collections.Generic;
using System.Windows;
using AOBot_Testing.Agents;
using AOBot_Testing.Structures;

namespace OceanyaClient.Features.Viewport
{
    /// <summary>
    /// Hosts the AO2 viewport surface inside the shared Oceanya window chrome.
    /// </summary>
    public partial class AO2ViewportWindowContent : OceanyaWindowContentControl
    {
        private readonly Dictionary<AOClient, AO2ViewportControl> profileControls = new Dictionary<AOClient, AO2ViewportControl>();
        private AOClient? activeClient;

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
            AttachClient(client, client);
        }

        /// <summary>
        /// Attaches the hosted viewport to a selected profile and the client that receives server IC echoes.
        /// </summary>
        public void AttachClient(AOClient? client, AOClient? incomingMessageClient)
        {
            AttachClient(client, incomingMessageClient, null, null);
        }

        /// <summary>
        /// Attaches the visible viewport to a selected profile and optional hidden-message filters.
        /// </summary>
        public void AttachClient(
            AOClient? client,
            AOClient? incomingMessageClient,
            Func<ICMessage, bool>? messageFilter,
            Func<string, bool>? actionFilter)
        {
            if (client == null)
            {
                activeClient = null;
                foreach (AO2ViewportControl control in profileControls.Values)
                {
                    control.Visibility = Visibility.Collapsed;
                }

                return;
            }

            EnsureClient(client, incomingMessageClient, messageFilter, actionFilter);
            activeClient = client;
            foreach (KeyValuePair<AOClient, AO2ViewportControl> pair in profileControls)
            {
                pair.Value.Visibility = ReferenceEquals(pair.Key, client)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Ensures a hidden viewport exists and continues receiving scene updates for the given profile.
        /// </summary>
        public void EnsureClient(
            AOClient client,
            AOClient? incomingMessageClient,
            Func<ICMessage, bool>? messageFilter,
            Func<string, bool>? actionFilter)
        {
            if (!profileControls.TryGetValue(client, out AO2ViewportControl? control))
            {
                control = new AO2ViewportControl
                {
                    Visibility = Visibility.Collapsed
                };
                profileControls[client] = control;
                ViewportHost.Children.Add(control);
            }

            control.AttachClient(client, incomingMessageClient, messageFilter, actionFilter);
            if (ReferenceEquals(activeClient, client))
            {
                control.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Removes a profile viewport and detaches it from AO2 events.
        /// </summary>
        public void RemoveClient(AOClient client)
        {
            if (!profileControls.Remove(client, out AO2ViewportControl? control))
            {
                return;
            }

            control.AttachClient(null, null);
            ViewportHost.Children.Remove(control);
            if (ReferenceEquals(activeClient, client))
            {
                activeClient = null;
            }
        }
    }
}
