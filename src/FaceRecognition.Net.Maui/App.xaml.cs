namespace FaceRecognition.Net.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        MainPage = new AppShell();
        // The camera loops only detect once this finishes. Without it the preview stays up
        // and the status chip never gets a line to show.
        _ = SdkSession.BootstrapAsync();
    }
}
