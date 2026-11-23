using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Views;

namespace FaceRecognition.Net.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    const int ReqFolder = 0x4652;
    const int ReqPhotos = 0x4653;
    const int MaxPhotos = 50;
    TaskCompletionSource<Android.Net.Uri?>? _folderTcs;
    TaskCompletionSource<IReadOnlyList<PickedPhoto>>? _photosTcs;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        KeyboardDismiss.NoteTouch(ev);
        return base.DispatchTouchEvent(ev);
    }

    internal Task<Android.Net.Uri?> PickFolderTreeAsync()
    {
        _folderTcs = new TaskCompletionSource<Android.Net.Uri?>();
        StartActivityForResult(new Intent(Intent.ActionOpenDocumentTree), ReqFolder);
        return _folderTcs.Task;
    }

    /// <summary>
    /// Photo picker (Android 13+) or OpenDocument. The bytes are copied here, while this
    /// activity still holds the read grant. MediaPicker resolves a storage path and then
    /// File.OpenRead throws "Access to the path is denied."
    /// </summary>
    internal Task<IReadOnlyList<PickedPhoto>> PickPhotosAsync(bool multiple)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<PickedPhoto>>();
        _photosTcs = tcs;
        try
        {
            StartActivityForResult(PhotoIntent(multiple), ReqPhotos);
        }
        catch (Exception ex)
        {
            _photosTcs = null;
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    static Intent PhotoIntent(bool multiple)
    {
        Intent intent;
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            intent = new Intent(MediaStore.ActionPickImages);
            intent.SetType("image/*");
            if (multiple)
            {
                intent.PutExtra(MediaStore.ExtraPickImagesMax, MaxPhotos);
            }
        }
        else
        {
            intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("image/*");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            if (multiple)
            {
                intent.PutExtra(Intent.ExtraAllowMultiple, true);
            }
        }

        return intent;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ReqFolder)
        {
            _folderTcs?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
            _folderTcs = null;
            return;
        }

        if (requestCode != ReqPhotos)
        {
            return;
        }

        var tcs = _photosTcs;
        _photosTcs = null;
        if (tcs == null)
        {
            return;
        }

        if (resultCode != Result.Ok || data == null)
        {
            tcs.TrySetResult(Array.Empty<PickedPhoto>());
            return;
        }

        try
        {
            tcs.TrySetResult(ReadPhotos(data));
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }

    IReadOnlyList<PickedPhoto> ReadPhotos(Intent data)
    {
        var resolver = ContentResolver;
        if (resolver == null)
        {
            return Array.Empty<PickedPhoto>();
        }

        var uris = new List<Android.Net.Uri>();
        var clip = data.ClipData;
        if (clip != null)
        {
            for (var i = 0; i < clip.ItemCount; i++)
            {
                var uri = clip.GetItemAt(i)?.Uri;
                if (uri != null)
                {
                    uris.Add(uri);
                }
            }
        }

        if (uris.Count == 0 && data.Data != null)
        {
            uris.Add(data.Data);
        }

        var photos = new List<PickedPhoto>();
        foreach (var uri in uris.Take(MaxPhotos))
        {
            using var input = resolver.OpenInputStream(uri);
            if (input == null)
            {
                continue;
            }

            using var memory = new MemoryStream();
            input.CopyTo(memory);
            photos.Add(new PickedPhoto(DisplayName(resolver, uri), memory.ToArray()));
        }

        return photos;
    }

    static string DisplayName(ContentResolver resolver, Android.Net.Uri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                var name = cursor.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return Path.GetFileName(name);
                }
            }
        }
        catch (Exception)
        {
            // The bytes were readable; the display name is only a label.
        }

        return "photo.jpg";
    }
}
