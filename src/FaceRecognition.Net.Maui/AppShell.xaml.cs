namespace FaceRecognition.Net.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        // The five tabs are declared in XAML; these are the pages pushed on top of one.
        Routing.RegisterRoute(nameof(CapturePage), typeof(CapturePage));
        Routing.RegisterRoute(nameof(ImportPage), typeof(ImportPage));
    }
}
