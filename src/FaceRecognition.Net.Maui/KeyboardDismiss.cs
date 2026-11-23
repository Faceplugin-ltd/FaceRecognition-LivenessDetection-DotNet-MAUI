#if IOS
using UIKit;
#endif
#if ANDROID
using Android.Views;
using Android.Views.InputMethods;
#endif

namespace FaceRecognition.Net.Maui;

/// <summary>
/// A tap that is not on a text field resigns the keyboard. Buttons still receive the tap.
/// </summary>
static class KeyboardDismiss
{
#if IOS
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIView, object> Attached = new();
#endif

    public static void Attach(ContentPage page, object? platformView)
    {
#if ANDROID
        _ = page;
        _ = platformView;
#endif
#if IOS
        if (platformView is not UIView view || !Attached.TryAdd(view, new object()))
        {
            return;
        }

        var tap = new UITapGestureRecognizer(() =>
        {
            view.EndEditing(true);
            page.Dispatcher.Dispatch(() => Unfocus(page));
        })
        {
            CancelsTouchesInView = false,
        };
        tap.ShouldRecognizeSimultaneously = (_, _) => true;
        tap.ShouldReceiveTouch = (_, touch) => !IsText(touch.View);
        view.AddGestureRecognizer(tap);
#endif
    }

#if ANDROID
    /// <summary>Called from the activity so a tap on a scrolling list still dismisses the keyboard.</summary>
    public static void NoteTouch(MotionEvent? ev)
    {
        if (ev?.Action != MotionEventActions.Down)
        {
            return;
        }

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var focus = activity?.CurrentFocus;
        if (focus == null || Inside(focus, ev.RawX, ev.RawY))
        {
            return;
        }

        var imm = activity?.GetSystemService(Android.Content.Context.InputMethodService) as InputMethodManager;
        imm?.HideSoftInputFromWindow(focus.WindowToken, HideSoftInputFlags.None);
        focus.ClearFocus();
        var page = CurrentPage();
        page?.Dispatcher.Dispatch(() => Unfocus(page));
    }

    static Page? CurrentPage()
    {
        var shell = Shell.Current;
        if (shell?.Navigation.ModalStack.LastOrDefault() is Page modal)
        {
            return modal;
        }

        return shell?.CurrentPage;
    }
#endif

    static void Unfocus(IVisualTreeElement node)
    {
        if (node is VisualElement visual && visual.IsFocused)
        {
            visual.Unfocus();
        }

        foreach (var child in node.GetVisualChildren())
        {
            Unfocus(child);
        }
    }

#if IOS
    static bool IsText(UIView? view)
    {
        while (view != null)
        {
            if (view is UITextField or UITextView or UISearchBar)
            {
                return true;
            }

            view = view.Superview;
        }

        return false;
    }
#elif ANDROID
    static bool Inside(Android.Views.View focus, float x, float y)
    {
        var loc = new int[2];
        focus.GetLocationOnScreen(loc);
        return x >= loc[0] && x <= loc[0] + focus.Width && y >= loc[1] && y <= loc[1] + focus.Height;
    }
#endif
}
