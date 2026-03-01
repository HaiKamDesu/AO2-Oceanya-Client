using System;

namespace OceanyaClient.Features.Startup
{
    public interface IStartupFunctionalityWindow
    {
        event Action? FinishedLoading;
    }
}
