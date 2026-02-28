using System;
using System.Windows;

namespace OceanyaClient.Features.Startup
{
    public static class StartupWindowLauncher
    {
        public static Window CreateStartupWindow(
            string? functionalityId,
            Action? onFunctionalityReady = null,
            Action? onFunctionalityClosed = null)
        {
            StartupFunctionalityOption selected = StartupFunctionalityCatalog.GetByIdOrDefault(functionalityId);
            Window startupWindow;
            if (selected.Id == StartupFunctionalityIds.CharacterDatabaseViewer)
            {
                startupWindow = new CharacterFolderVisualizerWindow(onAssetsRefreshed: null);
            }
            else if (selected.Id == StartupFunctionalityIds.CharacterFileCreator)
            {
                startupWindow = new AOCharacterFileCreatorWindow();
            }
            else if (selected.Id == StartupFunctionalityIds.EmptyWindowTemp)
            {
                startupWindow = new GenericOceanyaWindow
                {
                    Title = "Empty Window (temp)",
                    HeaderText = "Empty Window (temp)"
                };
            }
            else
            {
                startupWindow = new MainWindow();
            }

            if (onFunctionalityReady != null)
            {
                if (startupWindow is IStartupFunctionalityWindow notifyReady)
                {
                    notifyReady.FinishedLoading += onFunctionalityReady;
                }
                else
                {
                    startupWindow.ContentRendered += (_, _) => onFunctionalityReady();
                }
            }

            if (onFunctionalityClosed != null)
            {
                startupWindow.Closed += (_, _) => onFunctionalityClosed();
            }

            return startupWindow;
        }
    }
}
