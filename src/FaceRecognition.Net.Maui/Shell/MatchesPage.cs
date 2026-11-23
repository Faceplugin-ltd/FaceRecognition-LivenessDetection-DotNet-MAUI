namespace FaceRecognition.Net.Maui;

/// <summary>
/// Every gallery face the probe scored at or above the threshold, best first, with the score
/// bar beside each one. Tapping a row opens that person. Android `shell/MatchesActivity.kt`.
/// </summary>
public class MatchesPage : ContentPage
{
    public MatchesPage(IdentifyResult result)
    {
        Title = "Matches";
        BackgroundColor = DemoTheme.Bg;

        var matches = result.Matches;
        var header = ShellUi.Header(
            "Matches",
            ShellUi.Back(this),
            ShellUi.Pill(ShellUi.MatchesCount(matches.Count)));
        header.Padding = new Thickness(4, 4, 16, 0);
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
            Children =
            {
                new Label
                {
                    Text = $"{result.GallerySize} enrolled · threshold {ShellUi.Score(result.Threshold)}",
                    FontSize = 13,
                    TextColor = DemoTheme.Muted,
                },
            },
        };

        if (matches.Count == 0)
        {
            body.Children.Add(new Label
            {
                Text = "Nothing scored at or above the threshold.",
                FontSize = 14,
                TextColor = DemoTheme.Muted,
                Margin = new Thickness(0, 24, 0, 0),
            });
        }

        foreach (var candidate in matches)
        {
            body.Children.Add(MatchRow(candidate, result.Threshold));
        }

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

    View MatchRow(Candidate candidate, double threshold)
    {
        var record = FaceGallery.Get(candidate.Id);
        var photo = new Image
        {
            WidthRequest = 48,
            HeightRequest = 48,
            Aspect = Aspect.AspectFill,
            BackgroundColor = ShellUi.Card,
        };
        var path = record?.CropPath.Length > 0 ? record.CropPath : record?.OriginalPath ?? "";
        if (path.Length > 0 && File.Exists(path))
        {
            photo.Source = ImageSource.FromFile(path);
        }

        var text = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = $"{candidate.Name} · {ShellUi.IdTag(candidate.Id)}",
                    FontSize = 15,
                    TextColor = DemoTheme.Text,
                },
                ShellUi.Bar(
                    candidate.Similarity,
                    candidate.Similarity >= threshold ? ShellUi.StatusMatch : ShellUi.StatusNoMatch),
            },
        };

        var grid = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(new Border
        {
            Content = photo,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            WidthRequest = 48,
            HeightRequest = 48,
        });
        grid.Add(text, 1);
        grid.Add(ShellUi.Pill(ShellUi.Score(candidate.Similarity)), 2);

        var card = ShellUi.CardView(grid);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushModalAsync(new PersonSheetPage(candidate.Id));
        card.GestureRecognizers.Add(tap);
        return card;
    }
}
