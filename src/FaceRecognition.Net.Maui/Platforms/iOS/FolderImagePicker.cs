using Foundation;
using UniformTypeIdentifiers;
using UIKit;

namespace FaceRecognition.Net.Maui;

public static partial class FolderImagePicker
{
    static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "heic", "heif", "bmp", "webp",
    };

    static UIDocumentPickerViewController? _picker;

    static Task<IReadOnlyList<FolderImage>> PickPlatformAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<FolderImage>>();
        var picker = new UIDocumentPickerViewController(new[] { UTTypes.Folder });
        _picker = picker;
        picker.DidPickDocumentAtUrls += (_, e) =>
        {
            var folder = e.Urls.FirstOrDefault();
            _picker = null;
            tcs.TrySetResult(folder is null ? Array.Empty<FolderImage>() : List(folder));
        };
        picker.WasCancelled += (_, _) =>
        {
            _picker = null;
            tcs.TrySetResult(Array.Empty<FolderImage>());
        };

        UIViewController? host = null;
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene windowScene)
            {
                host = windowScene.Windows.FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
                if (host != null)
                {
                    break;
                }
            }
        }

        while (host?.PresentedViewController != null)
        {
            host = host.PresentedViewController;
        }

        host?.PresentViewController(picker, true, null);
        return tcs.Task;
    }

    static IReadOnlyList<FolderImage> List(NSUrl folder)
    {
        var access = folder.StartAccessingSecurityScopedResource();
        try
        {
            var path = folder.Path;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return Array.Empty<FolderImage>();
            }

            var tmp = Path.Combine(Path.GetTempPath(), "frs_import");
            Directory.CreateDirectory(tmp);
            var outList = new List<FolderImage>();
            foreach (var file in Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                var ext = Path.GetExtension(name).TrimStart('.');
                if (!Exts.Contains(ext))
                {
                    continue;
                }

                var dest = Path.Combine(tmp, name);
                var n = 1;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(tmp, $"{Path.GetFileNameWithoutExtension(name)}-{n}{Path.GetExtension(name)}");
                    n++;
                }

                File.Copy(file, dest, overwrite: false);
                var uri = NSUrl.FromFilename(dest).AbsoluteString ?? dest;
                outList.Add(new FolderImage(uri, Path.GetFileName(dest)));
            }

            return outList;
        }
        finally
        {
            if (access)
            {
                folder.StopAccessingSecurityScopedResource();
            }
        }
    }
}
