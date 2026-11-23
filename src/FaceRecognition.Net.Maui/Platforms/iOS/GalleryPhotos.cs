namespace FaceRecognition.Net.Maui;

public static partial class GalleryPhotos
{
    static async Task<IReadOnlyList<PickedPhoto>> PickPlatformAsync(bool multiple)
    {
        if (!multiple)
        {
            var one = await MediaPicker.Default.PickPhotoAsync();
            if (one == null)
            {
                return Array.Empty<PickedPhoto>();
            }

            return new[] { new PickedPhoto(one.FileName, await ReadAsync(one)) };
        }

        var picked = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Choose photos",
            FileTypes = FilePickerFileType.Images,
        });
        var list = new List<PickedPhoto>();
        foreach (var file in picked ?? Array.Empty<FileResult>())
        {
            list.Add(new PickedPhoto(file.FileName, await ReadAsync(file)));
        }

        return list;
    }

    static async Task<byte[]> ReadAsync(FileResult file)
    {
        await using var stream = await file.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }
}
