using Camera.MAUI;

namespace FaceRecognition.Net.Maui;

/// <summary>JPEG capture from Camera.MAUI for VideoWorker ingest.</summary>
static class CameraSnap
{
    public static async Task<byte[]?> CaptureJpegAsync(CameraView camera)
    {
        var path = System.IO.Path.Combine(
            FileSystem.CacheDirectory,
            $"snap_{Environment.TickCount64}.jpg");
        try
        {
            if (!await camera.SaveSnapShot(Camera.MAUI.ImageFormat.JPEG, path).ConfigureAwait(false))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
