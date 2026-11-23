namespace FaceRecognition.Net.Maui;

/// <summary>
/// Oval capture-done preview: crop <c>roiRect1</c> from the engine JPEG and
/// optionally mirror (front camera — engine is unmirrored, preview is mirrored).
/// Matches Android <c>CaptureView.setCapturedBitmap</c> / RN done-circle flip.
/// </summary>
static class CaptureRoiPreview
{
    public static byte[]? MakeJpeg(byte[] jpeg, double frameW, double frameH, bool mirror)
    {
        if (jpeg is not { Length: > 0 })
        {
            return null;
        }

        var (jw, jh) = CaptureLogic.JpegSize(jpeg);
        var fw = jw > 0 ? jw : (frameW > 0 ? frameW : 720);
        var fh = jh > 0 ? jh : (frameH > 0 ? frameH : 1280);
#if IOS
        return MakeIos(jpeg, fw, fh, mirror);
#elif ANDROID
        return MakeAndroid(jpeg, fw, fh, mirror);
#else
        return jpeg;
#endif
    }

    public static Microsoft.Maui.Graphics.IImage? MakeImage(byte[] jpeg, double frameW, double frameH, bool mirror)
    {
        var cropped = MakeJpeg(jpeg, frameW, frameH, mirror);
        if (cropped == null)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(cropped);
            return Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(stream);
        }
        catch
        {
            return null;
        }
    }

#if IOS
    static byte[]? MakeIos(byte[] jpeg, double fw, double fh, bool mirror)
    {
        try
        {
            using var data = Foundation.NSData.FromArray(jpeg);
            using var image = UIKit.UIImage.LoadFromData(data);
            if (image == null)
            {
                return null;
            }

            var pw = image.Size.Width * image.CurrentScale;
            var ph = image.Size.Height * image.CurrentScale;
            if (pw < 2 || ph < 2)
            {
                return null;
            }

            // Bake orientation into pixels (same as GalleryImagePrep).
            UIKit.UIGraphics.BeginImageContextWithOptions(new CoreGraphics.CGSize(pw, ph), true, 1);
            image.Draw(new CoreGraphics.CGRect(0, 0, pw, ph));
            using var oriented = UIKit.UIGraphics.GetImageFromCurrentImageContext();
            UIKit.UIGraphics.EndImageContext();
            if (oriented?.CGImage == null)
            {
                return null;
            }

            var roi = CaptureLogic.GetRoiRect1(fw, fh);
            var sx = pw / fw;
            var sy = ph / fh;
            var crop = new CoreGraphics.CGRect(
                Math.Round(roi.Left * sx),
                Math.Round(roi.Top * sy),
                Math.Round(roi.Width * sx),
                Math.Round(roi.Height * sy));
            if (crop.Width < 2 || crop.Height < 2)
            {
                return null;
            }

            using var cg = oriented.CGImage.WithImageInRect(crop);
            if (cg == null)
            {
                return null;
            }

            var size = new CoreGraphics.CGSize(crop.Width, crop.Height);
            UIKit.UIGraphics.BeginImageContextWithOptions(size, false, 1);
            var ctx = UIKit.UIGraphics.GetCurrentContext();
            if (ctx == null)
            {
                UIKit.UIGraphics.EndImageContext();
                return null;
            }

            ctx.AddEllipseInRect(new CoreGraphics.CGRect(0, 0, size.Width, size.Height));
            ctx.Clip();
            if (mirror)
            {
                // Android CaptureView.setCapturedBitmap always flips; we flip for front only.
                ctx.TranslateCTM(size.Width, 0);
                ctx.ScaleCTM(-1, 1);
            }

            using var clipped = new UIKit.UIImage(cg, 1, UIKit.UIImageOrientation.Up);
            clipped.Draw(new CoreGraphics.CGRect(0, 0, size.Width, size.Height));
            using var outImage = UIKit.UIGraphics.GetImageFromCurrentImageContext();
            UIKit.UIGraphics.EndImageContext();
            if (outImage == null)
            {
                return null;
            }

            using var outData = outImage.AsJPEG(0.92f);
            return outData?.ToArray();
        }
        catch
        {
            return null;
        }
    }
#endif

#if ANDROID
    static byte[]? MakeAndroid(byte[] jpeg, double fw, double fh, bool mirror)
    {
        try
        {
            using var full = Android.Graphics.BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length);
            if (full == null)
            {
                return null;
            }

            var roi = CaptureLogic.GetRoiRect1(fw, fh);
            var sx = full.Width / fw;
            var sy = full.Height / fh;
            var left = (int)Math.Round(roi.Left * sx);
            var top = (int)Math.Round(roi.Top * sy);
            var width = (int)Math.Round(roi.Width * sx);
            var height = (int)Math.Round(roi.Height * sy);
            left = Math.Clamp(left, 0, Math.Max(0, full.Width - 1));
            top = Math.Clamp(top, 0, Math.Max(0, full.Height - 1));
            width = Math.Clamp(width, 1, full.Width - left);
            height = Math.Clamp(height, 1, full.Height - top);

            using var cropped = Android.Graphics.Bitmap.CreateBitmap(full, left, top, width, height);
            if (cropped == null)
            {
                return null;
            }

            using var oval = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888!);
            using var canvas = new Android.Graphics.Canvas(oval);
            using var path = new Android.Graphics.Path();
            path.AddCircle(width / 2f, height / 2f, Math.Min(width, height) / 2f, Android.Graphics.Path.Direction.Ccw!);
            canvas.ClipPath(path);
            if (mirror)
            {
                canvas.Translate(width, 0);
                canvas.Scale(-1f, 1f);
            }

            canvas.DrawBitmap(cropped, 0, 0, null);
            using var ms = new MemoryStream();
            if (!oval.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 92, ms))
            {
                return null;
            }

            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
#endif
}
