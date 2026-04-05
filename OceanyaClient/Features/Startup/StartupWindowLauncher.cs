using System;
using System.Windows;

namespace OceanyaClient.Features.Startup
{
    public static class StartupWindowLauncher
    {
        public static Window CreateStartupWindow(
            string? functionalityId,
            Action? onFunctionalityReady = null,
            Action? onFunctionalityClosed = null,
            bool useSharedStartupWaitForm = false)
        {
            StartupFunctionalityOption selected = StartupFunctionalityCatalog.GetByIdOrDefault(functionalityId);
            Window startupWindow;
            IStartupFunctionalityWindow? startupFunctionalityWindow = null;
            if (selected.Id == StartupFunctionalityIds.CharacterDatabaseViewer)
            {
                CharacterFolderVisualizerWindow content = new CharacterFolderVisualizerWindow(
                    onAssetsRefreshed: null,
                    suppressInitialLoadWaitForm: useSharedStartupWaitForm);
                startupFunctionalityWindow = content;
                startupWindow = OceanyaWindowManager.CreateWindow(content);
            }
            else if (selected.Id == StartupFunctionalityIds.CharacterFileCreator)
            {
                AOCharacterFileCreatorWindow content = new AOCharacterFileCreatorWindow();
                startupFunctionalityWindow = content;
                startupWindow = OceanyaWindowManager.CreateWindow(content);
            }
            else if (selected.Id == StartupFunctionalityIds.OceanyanFileHivemind)
            {
                OceanyanFileHivemindWindow content = new OceanyanFileHivemindWindow(
                    manageStartupWaitForm: !useSharedStartupWaitForm);
                startupFunctionalityWindow = content;
                startupWindow = OceanyaWindowManager.CreateWindow(content);
            }
            else
            {
                MainWindow content = new MainWindow(
                    aiModeEnabled: string.Equals(
                        selected.Id,
                        StartupFunctionalityIds.Ao2AiBot,
                        StringComparison.OrdinalIgnoreCase));
                startupFunctionalityWindow = content;
                startupWindow = OceanyaWindowManager.CreateWindow(content);
            }

            if (onFunctionalityReady != null)
            {
                if (startupFunctionalityWindow != null)
                {
                    startupFunctionalityWindow.FinishedLoading += onFunctionalityReady;
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
