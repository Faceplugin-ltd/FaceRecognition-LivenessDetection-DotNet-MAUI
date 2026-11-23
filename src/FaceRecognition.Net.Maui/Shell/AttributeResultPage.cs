namespace FaceRecognition.Net.Maui;

/// <summary>
/// Everything the engine read from the probe, laid out like
/// <c>activity_attribute_result.xml</c>: the photo, then a section per face with its
/// authenticity, quality, pose, eyes and expression, and the closest person in the gallery.
/// </summary>
public class AttributeResultPage : ContentPage
{
    /// <summary>Eyes read as open at or above this score — a demo threshold, not a setting.</summary>
    const double EyeOpen = 0.25;

    public AttributeResultPage(AttributeResult result)
    {
        Title = "Attribute result";
        BackgroundColor = DemoTheme.Bg;

        var header = ShellUi.Header(
            "Attribute result",
            ShellUi.Back(this),
            ShellUi.Pill(ShellUi.FacesCount(result.Faces.Count)));
        header.Padding = new Thickness(4, 4, 16, 0);
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
        };

        if (result.ProbeJpeg.Length > 0)
        {
            body.Children.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = 12,
                },
                BackgroundColor = ShellUi.Card,
                HeightRequest = 240,
                Content = new Image
                {
                    Source = ImageSource.FromStream(() => new MemoryStream(result.ProbeJpeg)),
                    Aspect = Aspect.AspectFit,
                },
            });
        }

        foreach (var face in result.Faces)
        {
            body.Children.Add(FaceSections(face, result.Threshold));
        }

        if (result.Faces.Count == 0)
        {
            body.Children.Add(new Label
            {
                Text = FaceGallery.NoFace,
                FontSize = 14,
                TextColor = DemoTheme.Muted,
            });
        }

        body.Children.Add(ShellUi.StatusCard(result.Report, "Attribute status"));
        var scroll = new ScrollView { Content = body };
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children = { header, scroll },
        };
        Grid.SetRow(scroll, 1);
    }

    static View FaceSections(FaceAttributes face, double threshold)
    {
        var b = face.Box;
        var rows = new VerticalStackLayout { Spacing = 0 };

        var live = DemoSettings.IsLive(b.Liveness);
        rows.Children.Add(ShellUi.Row(
            "Liveness",
            ShellUi.Score(b.Liveness ?? double.NaN),
            (live ? "Live" : "Fake", live ? ShellUi.Badge.Pass : ShellUi.Badge.Fail),
            b.Liveness));
        rows.Children.Add(ShellUi.Row(
            "Quality",
            ShellUi.Score(b.FaceQuality ?? double.NaN),
            null,
            b.FaceQuality));
        rows.Children.Add(ShellUi.Row("Yaw", ShellUi.Angle(b.Yaw ?? double.NaN)));
        rows.Children.Add(ShellUi.Row("Roll", ShellUi.Angle(b.Roll ?? double.NaN)));
        rows.Children.Add(ShellUi.Row("Pitch", ShellUi.Angle(b.Pitch ?? double.NaN)));

        // One eyes row: the engine scores eye openness once per face, so a Left / Right pair
        // would print the same number twice.
        var open = 1.0 - (b.LeftEyeClosed ?? 0);
        rows.Children.Add(ShellUi.Row(
            "Eyes open",
            $"{ShellUi.Score(open)}  (≥ {ShellUi.Score(EyeOpen)})",
            (open >= EyeOpen ? "Open" : "Closed",
             open >= EyeOpen ? ShellUi.Badge.Pass : ShellUi.Badge.Warn),
            open));

        if (!string.IsNullOrWhiteSpace(b.EmotionLabel))
        {
            rows.Children.Add(ShellUi.Row("Emotion", b.EmotionLabel!.Trim()));
        }
        if (b.Age is { } age && age > 0)
        {
            rows.Children.Add(ShellUi.Row("Age", ((int)age).ToString()));
        }
        if (!string.IsNullOrWhiteSpace(b.GenderLabel))
        {
            rows.Children.Add(ShellUi.Row("Gender", b.GenderLabel!.Trim()));
        }
        rows.Children.Add(ShellUi.Row("Template", $"{face.TemplateBytes} bytes"));

        if (face.Closest is { } hit)
        {
            var isMatch = hit.Similarity >= threshold;
            rows.Children.Add(ShellUi.Row(
                "Closest",
                $"{hit.Name} · {ShellUi.IdTag(hit.Id)}",
                (isMatch ? "Match" : "Below", isMatch ? ShellUi.Badge.Pass : ShellUi.Badge.Warn),
                hit.Similarity));
        }

        return ShellUi.Section($"Face {face.Index + 1}", rows);
    }
}
