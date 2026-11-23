using Android.Content;
using Android.Provider;

namespace FaceRecognition.Net.Maui;

public static partial class FolderImagePicker
{
    static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "heic", "heif", "bmp", "webp",
    };

    static async Task<IReadOnlyList<FolderImage>> PickPlatformAsync()
    {
        if (Platform.CurrentActivity is not MainActivity activity)
        {
            return Array.Empty<FolderImage>();
        }

        var tree = await activity.PickFolderTreeAsync();
        if (tree is null)
        {
            return Array.Empty<FolderImage>();
        }

        try
        {
            activity.ContentResolver?.TakePersistableUriPermission(
                tree,
                ActivityFlags.GrantReadUriPermission);
        }
        catch
        {
            // persist is optional; listing still works for this session
        }

        return List(activity, tree);
    }

    static IReadOnlyList<FolderImage> List(MainActivity activity, Android.Net.Uri tree)
    {
        var resolver = activity.ContentResolver;
        if (resolver is null)
        {
            return Array.Empty<FolderImage>();
        }

        var destDir = Path.Combine(activity.CacheDir!.AbsolutePath, "frs_import");
        Directory.CreateDirectory(destDir);

        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(
            tree,
            DocumentsContract.GetTreeDocumentId(tree));
        var outList = new List<FolderImage>();
        try
        {
            using var cursor = resolver.Query(
                children,
                new[]
                {
                    DocumentsContract.Document.ColumnDocumentId,
                    DocumentsContract.Document.ColumnDisplayName,
                    DocumentsContract.Document.ColumnMimeType,
                },
                null,
                null,
                null);
            if (cursor is null)
            {
                return Array.Empty<FolderImage>();
            }

            while (cursor.MoveToNext())
            {
                var docId = cursor.GetString(0);
                var name = cursor.GetString(1);
                var mime = cursor.GetString(2) ?? "";
                if (string.IsNullOrEmpty(docId) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
                if (!(mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || Exts.Contains(ext)))
                {
                    continue;
                }

                var docUri = DocumentsContract.BuildDocumentUriUsingTree(tree, docId);
                if (docUri is null)
                {
                    continue;
                }

                var dest = UniquePath(destDir, name);
                using (var input = resolver.OpenInputStream(docUri))
                {
                    if (input is null)
                    {
                        continue;
                    }

                    using var output = File.Create(dest);
                    input.CopyTo(output);
                }

                outList.Add(new FolderImage($"file://{dest}", Path.GetFileName(dest)));
            }
        }
        catch
        {
            return Array.Empty<FolderImage>();
        }

        return outList
            .OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string UniquePath(string dir, string name)
    {
        var dest = Path.Combine(dir, name);
        var n = 1;
        while (File.Exists(dest))
        {
            dest = Path.Combine(
                dir,
                $"{Path.GetFileNameWithoutExtension(name)}-{n}{Path.GetExtension(name)}");
            n++;
        }
        return dest;
    }
}
