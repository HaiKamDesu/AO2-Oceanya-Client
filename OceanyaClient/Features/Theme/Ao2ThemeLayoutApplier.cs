using System;
using System.Collections.Generic;
using System.Linq;
using Common;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Writes an imported AO2 theme into the savefile as the active Oceanya panel layout.
    /// </summary>
    /// <remarks>
    /// Two callers import themes - the Settings window's "Import layout from AO2 theme" button and the
    /// theme-capture sweep (<see cref="Ao2ThemeCaptureRunner"/>) - and a capture is only worth anything if
    /// it shows exactly what the button produces. Keeping the savefile and <c>config.ini</c> writes here
    /// means the two cannot drift apart; each caller still does its own UI refresh afterwards.
    /// </remarks>
    public static class Ao2ThemeLayoutApplier
    {
        /// <summary>
        /// Imports <paramref name="themeName"/> and stores it as the current layout.
        /// </summary>
        /// <param name="themeName">AO2 theme folder name.</param>
        /// <param name="configValues">
        /// Loaded <c>config.ini</c> values. The theme scaling factor is read from it, and its <c>theme</c>
        /// key is rewritten and saved so the viewport, chatbox and court sounds follow the same theme.
        /// </param>
        /// <returns>The import result, or <c>null</c> when the theme has no <c>courtroom_design.ini</c>.</returns>
        public static Ao2ThemeImportResult? ApplyToSavefile(string themeName, IDictionary<string, string> configValues)
        {
            if (string.IsNullOrWhiteSpace(themeName))
            {
                return null;
            }

            if (configValues == null)
            {
                throw new ArgumentNullException(nameof(configValues));
            }

            int scalingFactor = Math.Max(1, Ao2ConfigIniSettings.GetInt(configValues, "theme_scaling_factor", 1));
            Ao2ThemeImportResult? result = Ao2ThemeLayoutImporter.ImportTheme(themeName, scalingFactor);
            if (result == null)
            {
                return null;
            }

            SaveFile.Data.OceanyaThemeLayout = result.Value.Layout;
            // Every AO2 theme places the viewport inside the window, so that mode comes along with it, and
            // the same is true of the area and music lists whenever the theme gives them a rectangle.
            SaveFile.Data.GMViewportRenderInPanel = true;
            SaveFile.Data.GMAreaListRenderInPanel = result.Value.ImportedPanelIds.Contains(OceanyaPanelCatalog.AreaListPanelId);
            SaveFile.Data.GMMusicListRenderInPanel = result.Value.ImportedPanelIds.Contains(OceanyaPanelCatalog.MusicListPanelId);

            // Every AO2 theme puts its chatbox rectangle ON TOP of its viewport rectangle - that is simply
            // how a courtroom is laid out there. With the chatbox pushed below the viewport instead, every
            // imported theme came out taller than the theme asked for and the chatbox landed in the bottom
            // bar rather than over the sprite, so the translation only ever looked right with this on.
            SaveFile.Data.GMViewportChatboxOverlapsViewport = true;

            // Both lists share one rectangle, exactly as they do in AO2 - and AO2 opens on the MUSIC list:
            // `courtroom.cpp` calls `ui_area_list->hide()` at construction and the A/M button swaps them.
            // We were opening on the area list, so the green track list every theme is designed around was
            // missing from the shot entirely.
            if (SaveFile.Data.GMAreaListRenderInPanel && SaveFile.Data.GMMusicListRenderInPanel)
            {
                SaveFile.Data.GMAreaMusicSlotShowsMusic = true;
            }
            SaveFile.Save();

            // The chatbox, in-viewport art, colours and sounds already come from the AO2 theme selected
            // in config.ini, so point that at the same theme or half the look stays on the old one.
            configValues["theme"] = themeName;
            Ao2ConfigIniSettings.Save(configValues);

            return result;
        }
    }
}
