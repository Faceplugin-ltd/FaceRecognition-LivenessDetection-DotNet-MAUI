namespace FaceRecognition.Net.Maui;

/// <summary>One photo the user picked. Bytes are already in memory, so later reads do not touch the gallery.</summary>
public readonly record struct PickedPhoto(string FileName, byte[] Bytes);

/// <summary>
/// Gallery photo pick. Android copies the bytes inside the activity result, while the
/// picker grant is still valid. Opening the resolved storage path afterwards is denied.
/// </summary>
public static partial class GalleryPhotos
{
    public static Task<IReadOnlyList<PickedPhoto>> PickAsync(bool multiple) =>
        PickPlatformAsync(multiple);
}
