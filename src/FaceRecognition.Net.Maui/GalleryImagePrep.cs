namespace FaceRecognition.Net.Maui;

/// <summary>
/// Gallery still prep matching native demos:
/// EXIF/orientation bake + long side ≤ 1280 (<c>enginePreparedImage</c>).
/// Detect returns boxes in that prepared space — crop/extract must use the same JPEG.
/// </summary>
static class GalleryImagePrep
{
    public const int EngineMaxSide = 1280;

    /// <summary>Orient + downscale (preferred for Attribute / Enroll gallery).</summary>
    public static byte[] PrepareEngineJpeg(byte[] jpeg) =>
        Prepare(jpeg, EngineMaxSide);

    /// <summary>Orient only (no downscale).</summary>
    public static byte[] OrientJpeg(byte[] jpeg) =>
        Prepare(jpeg, maxSide: 0);

    static byte[] Prepare(byte[] jpeg, int maxSide)
    {
        if (jpeg is not { Length: > 0 })
        {
            return jpeg;
        }

#if IOS
        return PrepareIos(jpeg, maxSide);
#elif ANDROID
        return PrepareAndroid(jpeg, maxSide);
#else
        return jpeg;
#endif
    }

#if IOS
    static byte[] PrepareIos(byte[] jpeg, int maxSide)
    {
        try
        {
            using var data = Foundation.NSData.FromArray(jpeg);
            using var image = UIKit.UIImage.LoadFromData(data);
            if (image == null)
            {
                return jpeg;
            }

            // Pixel bake like RN FRSFixOrientation (not points × scale mismatch).
            var pw = image.Size.Width * image.CurrentScale;
            var ph = image.Size.Height * image.CurrentScale;
            if (pw < 2 || ph < 2)
            {
                return jpeg;
            }

            nfloat tw = pw;
            nfloat th = ph;
            if (maxSide > 0)
            {
                var longSide = (nfloat)Math.Max(pw, ph);
                if (longSide > maxSide)
                {
                    var scale = maxSide / longSide;
                    tw = (nfloat)Math.Round(pw * scale);
                    th = (nfloat)Math.Round(ph * scale);
                }
            }

            tw = (nfloat)Math.Max(1, tw);
            th = (nfloat)Math.Max(1, th);
            UIKit.UIGraphics.BeginImageContextWithOptions(new CoreGraphics.CGSize(tw, th), true, 1);
            image.Draw(new CoreGraphics.CGRect(0, 0, tw, th));
            using var outImage = UIKit.UIGraphics.GetImageFromCurrentImageContext();
            UIKit.UIGraphics.EndImageContext();
            if (outImage == null)
            {
                return jpeg;
            }

            using var outData = outImage.AsJPEG(0.92f);
            if (outData == null)
            {
                return jpeg;
            }

            var bytes = new byte[outData.Length];
            System.Runtime.InteropServices.Marshal.Copy(outData.Bytes, bytes, 0, (int)outData.Length);
            return bytes;
        }
        catch
        {
            return jpeg;
        }
    }
#endif

#if ANDROID
    static byte[] PrepareAndroid(byte[] jpeg, int maxSide)
    {
        try
        {
            var opts = new Android.Graphics.BitmapFactory.Options
            {
                InPreferredConfig = Android.Graphics.Bitmap.Config.Argb8888,
            };
            using var decoded = Android.Graphics.BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length, opts);
            if (decoded == null)
            {
                return jpeg;
            }

            var degrees = 0;
            try
            {
                using var input = new System.IO.MemoryStream(jpeg);
                using var exif = new Android.Media.ExifInterface(input);
                degrees = exif.GetAttributeInt(
                    Android.Media.ExifInterface.TagOrientation,
                    (int)Android.Media.Orientation.Normal) switch
                {
                    (int)Android.Media.Orientation.Rotate90 => 90,
                    (int)Android.Media.Orientation.Rotate180 => 180,
                    (int)Android.Media.Orientation.Rotate270 => 270,
                    _ => 0,
                };
            }
            catch
            {
            }

            Android.Graphics.Bitmap working = decoded;
            if (degrees != 0)
            {
                using var matrix = new Android.Graphics.Matrix();
                matrix.PostRotate(degrees);
                working = Android.Graphics.Bitmap.CreateBitmap(
                    decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
                if (!ReferenceEquals(working, decoded))
                {
                    decoded.Recycle();
                }
            }

            if (maxSide > 0)
            {
                var longSide = Math.Max(working.Width, working.Height);
                if (longSide > maxSide)
                {
                    var scale = maxSide / (float)longSide;
                    var tw = Math.Max(1, (int)Math.Round(working.Width * scale));
                    var th = Math.Max(1, (int)Math.Round(working.Height * scale));
                    var scaled = Android.Graphics.Bitmap.CreateScaledBitmap(working, tw, th, true);
                    if (!ReferenceEquals(scaled, working))
                    {
                        working.Recycle();
                        working = scaled;
                    }
                }
            }

            using var stream = new System.IO.MemoryStream();
            // Re-encoded JPEG has no orientation tag — safe for FaceSdk.Decode EXIF path.
            working.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, 92, stream);
            working.Recycle();
            return stream.ToArray();
        }
        catch
        {
            return jpeg;
        }
    }
#endif
}
