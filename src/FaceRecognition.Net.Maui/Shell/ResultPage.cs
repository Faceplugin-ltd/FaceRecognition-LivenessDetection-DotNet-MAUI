namespace FaceRecognition.Net.Maui;

/// <summary>
/// What the 1:N search found, laid out like <c>activity_identify_result.xml</c>: a Match /
/// No match pill, the probe beside the best hit, the name and ID with a subline, the extract
/// / search / gallery metrics, the status card, then the matches with a count.
/// </summary>
public class ResultPage : ContentPage
{
    /// <summary>Matches shown before the list is folded behind "Show all".</summary>
    const int FirstPage = 5;

    public ResultPage(IdentifyResult result)
    {
        Title = "Identify result";
        BackgroundColor = DemoTheme.Bg;

        var primary = result.Primary;
        var best = primary?.Best;
        var matched = best != null && best.Similarity >= result.Threshold;

        var header = ShellUi.Header("Identify result", ShellUi.Back(this));
        header.Padding = new Thickness(4, 4, 16, 0);
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
            Children =
            {
                ShellUi.Pill(
                    matched ? "Match" : "No match",
                    matched ? ShellUi.StatusMatch : ShellUi.StatusNoMatch,
                    14),
                Photos(result, best),
                Headline(best, matched, result.Threshold),
                ShellUi.MetricsRow(
                    ShellUi.MetricTile(ShellUi.Ms(result.ExtractMs), "Extract"),
                    ShellUi.MetricTile(ShellUi.Ms(result.SearchMs), "Search"),
                    ShellUi.MetricTile(result.GallerySize.ToString(), "Gallery")),
                ShellUi.StatusCard(result.Report, "Identify status"),
            },
        };

        if (result.Faces.Count > 1)
        {
            var faces = new VerticalStackLayout { Spacing = 6 };
            foreach (var face in result.Faces)
            {
                var hit = face.Best;
                faces.Children.Add(ShellUi.Row(
                    $"Face {face.Index + 1}",
                    hit == null
                        ? "no match"
                        : $"{hit.Name} · {ShellUi.IdTag(hit.Id)} · {ShellUi.Score(hit.Similarity)}",
                    null,
                    hit?.Similarity));
            }
            body.Children.Add(ShellUi.Section("Probe faces", faces));
        }

        var matches = result.Matches;
        body.Children.Add(MatchesHeader(matches.Count, result));
        if (matches.Count == 0)
        {
            body.Children.Add(new Label
            {
                Text = "Nothing scored at or above the threshold.",
                FontSize = 14,
                TextColor = DemoTheme.Muted,
            });
        }
        foreach (var candidate in matches.Take(FirstPage))
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

    View Photos(IdentifyResult result, Candidate? best)
    {
        var probe = Square(
            result.ProbeJpeg.Length > 0
                ? ImageSource.FromStream(() => new MemoryStream(result.ProbeJpeg))
                : null,
            "Probe");

        ImageSource? enrolled = null;
        if (best != null)
        {
            var record = FaceGallery.Get(best.Id);
            var path = record?.CropPath.Length > 0 ? record.CropPath : record?.OriginalPath ?? "";
            if (path.Length > 0 && File.Exists(path))
            {
                enrolled = ImageSource.FromFile(path);
            }
        }

        var grid = new Grid
        {
            ColumnSpacing = 16,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(probe);
        grid.Add(Square(enrolled, "Rank 1"), 1);
        return grid;
    }

    static View Square(ImageSource? source, string label) => new VerticalStackLayout
    {
        Spacing = 6,
        Children =
        {
            new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = 12,
                },
                BackgroundColor = ShellUi.Card,
                HeightRequest = 160,
                Content = new Image
                {
                    Source = source,
                    Aspect = Aspect.AspectFill,
                },
            },
            new Label
            {
                Text = label,
                FontSize = 12,
                TextColor = DemoTheme.Muted,
                HorizontalTextAlignment = TextAlignment.Center,
            },
        },
    };

    static View Headline(Candidate? best, bool matched, double threshold)
    {
        var name = new Label
        {
            Text = best?.Name ?? "No match",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = DemoTheme.Text,
            VerticalOptions = LayoutOptions.Center,
        };
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        row.Add(name);
        if (best != null)
        {
            row.Add(ShellUi.Pill(ShellUi.IdTag(best.Id)), 1);
        }

        var subline = best == null
            ? "No one in the gallery scored against this face."
            : $"Similarity {ShellUi.Score(best.Similarity)} · threshold {ShellUi.Score(threshold)}"
              + (matched ? "" : " · below the threshold");

        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                row,
                new Label { Text = subline, FontSize = 13, TextColor = DemoTheme.Muted },
            },
        };
    }

    View MatchesHeader(int count, IdentifyResult result)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(new Label
        {
            Text = "Matches",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = DemoTheme.Text,
            VerticalOptions = LayoutOptions.Center,
        });
        grid.Add(new Label
        {
            Text = ShellUi.MatchesCount(count),
            FontSize = 13,
            TextColor = DemoTheme.Muted,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalOptions = LayoutOptions.Center,
        }, 1);

        if (count > FirstPage)
        {
            var more = new Button
            {
                Text = "Show all",
                TextColor = DemoTheme.Accent,
                BackgroundColor = Colors.Transparent,
            };
            more.Clicked += async (_, _) => await Navigation.PushAsync(new MatchesPage(result));
            grid.Add(more, 2);
        }
        return grid;
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
        grid.Add(new VerticalStackLayout
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
                    candidate.Similarity >= threshold
                        ? ShellUi.StatusMatch
                        : ShellUi.StatusNoMatch),
            },
        }, 1);
        grid.Add(ShellUi.Pill(ShellUi.Score(candidate.Similarity)), 2);

        var card = ShellUi.CardView(grid);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushModalAsync(new PersonSheetPage(candidate.Id));
        card.GestureRecognizers.Add(tap);
        return card;
    }
}
