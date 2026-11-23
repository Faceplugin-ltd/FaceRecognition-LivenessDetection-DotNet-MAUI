namespace FaceRecognition.Net.Maui;

public sealed class FolderImage
{
    public FolderImage(string uri, string fileName)
    {
        Uri = uri;
        FileName = fileName;
    }

    public string Uri { get; }
    public string FileName { get; }
}

/// <summary>SAF / UIDocumentPicker folder listing, same rule as the native Android and iOS apps.</summary>
public static partial class FolderImagePicker
{
    public static Task<IReadOnlyList<FolderImage>> PickAsync() => PickPlatformAsync();
}
