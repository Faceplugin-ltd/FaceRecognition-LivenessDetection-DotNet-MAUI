namespace FaceRecognition.Net.Maui;

/// <summary>
/// Live JPEG → VideoWorker prep. Camera.MAUI snapshots are already upright portrait
/// (unlike RN VisionCamera, which needs front +180° on iOS).
/// Landscape stills still get ±90° like native CameraFrameUtils.
/// </summary>
static class LiveFramePrep
{
    public const int MaxEdge = 640;

    public readonly record struct Plan(int RotateDegrees, int MaxEdge);

    public static Plan PlanFor(bool frontCamera, double width, double height)
    {
        var rotate = 0;
        if (width > height)
        {
            // iOS back +90° was 180° from the front camera's −90°. Android back still needs +90°.
            var android = DeviceInfo.Current.Platform == DevicePlatform.Android;
            rotate = !frontCamera && android ? 90 : -90;
        }

        return new Plan(rotate, MaxEdge);
    }
}

public readonly record struct LiveFrameResult(bool Ingested, int Width, int Height, byte[]? Jpeg);
