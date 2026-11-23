using Camera.MAUI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace FaceRecognition.Net.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // One title row, drawn by the page. The shell bar was a second copy of the same name
        // and it hid the back control on screens pushed from a tab.
        Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping("FaceChrome", (handler, view) =>
        {
            if (view is not Microsoft.Maui.Controls.Page page)
            {
                return;
            }

            Shell.SetNavBarIsVisible(page, false);
            Microsoft.Maui.Controls.NavigationPage.SetHasNavigationBar(page, false);
            if (page is ContentPage content)
            {
                if (page is not CapturePage and not LivenessPage)
                {
                    content.On<iOS>().SetUseSafeArea(true);
                }

                KeyboardDismiss.Attach(content, handler.PlatformView);
            }
        });

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCameraView();
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
