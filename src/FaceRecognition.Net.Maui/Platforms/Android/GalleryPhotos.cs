namespace FaceRecognition.Net.Maui;

public static partial class GalleryPhotos
{
    static Task<IReadOnlyList<PickedPhoto>> PickPlatformAsync(bool multiple)
    {
        if (Platform.CurrentActivity is not MainActivity activity)
        {
            return Task.FromResult<IReadOnlyList<PickedPhoto>>(Array.Empty<PickedPhoto>());
        }

        return activity.PickPhotosAsync(multiple);
    }
}
