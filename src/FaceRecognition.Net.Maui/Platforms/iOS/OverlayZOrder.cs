using UIKit;

namespace FaceRecognition.Net.Maui;

/// <summary>
/// Camera.MAUI's native preview UIView sits above managed siblings on iOS.
/// Bring overlay PlatformViews to the front of the shared superview (native CaptureView pattern).
/// </summary>
static class OverlayZOrder
{
    public static void BringToFront(params View[] views)
    {
        void Apply()
        {
            UIView? parent = null;
            var natives = new List<UIView>();
            foreach (var view in views)
            {
                if (view.Handler?.PlatformView is not UIView native)
                {
                    continue;
                }

                parent ??= native.Superview;
                natives.Add(native);
            }

            if (parent == null)
            {
                return;
            }

            foreach (var native in natives)
            {
                if (native.Superview == parent)
                {
                    parent.BringSubviewToFront(native);
                }
            }
        }

        if (MainThread.IsMainThread)
        {
            Apply();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Apply);
        }
    }
}
