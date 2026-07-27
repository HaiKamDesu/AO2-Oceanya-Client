using System;
using System.Windows.Controls;
using Common;
using Common.WebAssets;

namespace OceanyaClient.Utilities
{
    /// <summary>
    /// Adjusts context menus for assets that are streamed from the server's asset URL rather than
    /// installed on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Web assets live in a private cache mirror, not in the user's AO install, so filesystem actions
    /// either mislead ("Open in file explorer" opens a cache folder the user never chose) or are
    /// outright destructive ("Delete character folder" deletes a cache entry that silently reappears).
    /// </para>
    /// <para>
    /// The goal is that the two sources feel identical until the user goes looking. Nothing here
    /// announces web assets on its own: menus only change when the entry would otherwise be wrong, and
    /// the replacement text explains why in a single line.
    /// </para>
    /// </remarks>
    public static class WebAssetMenuDecorator
    {
        /// <summary>Section header used wherever a menu exposes web asset provenance.</summary>
        public const string SectionTitle = "Server assets (web)";

        /// <summary>Text shown in place of a filesystem action that cannot apply to a web asset.</summary>
        public const string StreamedItemHeader = "Streamed from the server - no local folder";

        /// <summary>Reports whether <paramref name="path"/> was streamed from the server.</summary>
        public static bool IsWebAsset(string? path) => WebAssetSource.IsWebAsset(path);

        /// <summary>
        /// Replaces a filesystem menu item with a disabled explanation when its target is a web asset.
        /// Leaves the item untouched for a normal local path, so nothing changes for local content.
        /// </summary>
        /// <returns><c>true</c> when the item was rewritten for a web asset.</returns>
        public static bool ApplyProvenance(MenuItem? item, string? path)
        {
            if (item == null || !IsWebAsset(path))
            {
                return false;
            }

            item.Header = StreamedItemHeader;
            item.IsEnabled = false;
            item.ToolTip = "This asset is provided by the server you are connected to, so it has no folder "
                + "in your Attorney Online install.";
            return true;
        }

        /// <summary>
        /// Appends a "Copy asset URL" action when <paramref name="path"/> is a web asset, giving the
        /// user something actionable in place of the filesystem entries that were removed.
        /// </summary>
        public static void AddCopyAssetUrlItem(ItemsControl? menu, string? path)
        {
            if (menu == null || !IsWebAsset(path))
            {
                return;
            }

            string url = WebAssetService.Current?.Source.TryGetRemoteUrl(path) ?? string.Empty;
            MenuItem item = new MenuItem
            {
                Header = "Copy asset URL",
                IsEnabled = url.Length > 0
            };
            item.Click += (_, _) => ClipboardUtilities.TrySetText(url);
            menu.Items.Add(item);
        }

        /// <summary>
        /// Adds the full provenance section (header plus "Copy asset URL") for a web asset. No-op for a
        /// local path, so callers can invoke it unconditionally.
        /// </summary>
        public static void AddProvenanceSection(ItemsControl? menu, string? path)
        {
            if (menu == null || !IsWebAsset(path))
            {
                return;
            }

            ContextMenuSectionHelper.AddHeader(menu, SectionTitle, addLeadingSeparator: true);
            AddCopyAssetUrlItem(menu, path);
        }
    }
}
