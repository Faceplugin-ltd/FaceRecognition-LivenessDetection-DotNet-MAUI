namespace FaceRecognition.Net.Maui;

static class OverlayZOrder
{
    public static void BringToFront(params View[] views)
    {
        foreach (var view in views)
        {
            view.ZIndex = Math.Max(view.ZIndex, 50);
        }
    }
}
