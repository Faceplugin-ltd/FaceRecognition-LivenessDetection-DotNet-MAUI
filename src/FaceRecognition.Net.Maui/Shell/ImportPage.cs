namespace FaceRecognition.Net.Maui;

/// <summary>
/// Bulk enrolment: pick a batch of photos, enroll each one, and list what happened to it.
/// One row per photo, with the enrolment status behind a tap. Android `shell/ImportActivity.kt`.
/// </summary>
public class ImportPage : ContentPage
{
    /// <summary>Enough to show the 1:N search working without a minutes-long run.</summary>
    const int MaxPhotos = 50;

    readonly Label _summary = new() { FontSize = 13, TextColor = DemoTheme.Muted };
    readonly VerticalStackLayout _rows = new() { Spacing = 8 };
    readonly ActivityIndicator _busy = new() { IsVisible = false, IsRunning = false };
    readonly Button _pick;

    public ImportPage()
    {
        Title = "Bulk enroll from photos";
        BackgroundColor = DemoTheme.Bg;

        _pick = new Button
        {
            Text = "Choose photos",
            TextColor = Colors.White,
            BackgroundColor = DemoTheme.Tile,
            CornerRadius = 24,
            HeightRequest = 48,
        };
        _pick.Clicked += async (_, _) => await RunAsync();

        var header = ShellUi.Header("Bulk enroll from photos", ShellUi.Back(this));
        header.Padding = new Thickness(4, 4, 16, 0);
        var scroll = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = new Thickness(16),
                Children =
                {
                    new Label
                    {
                        Text = $"Each photo is enrolled as one person. Up to {MaxPhotos} at a time.",
                        FontSize = 13,
                        TextColor = DemoTheme.Muted,
                    },
                    _pick,
                    _busy,
                    _summary,
                    _rows,
                },
            },
        };
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

    async Task RunAsync()
    {
        var source = await DisplayActionSheet(
            "Bulk enroll from photos",
            "Cancel",
            null,
            "Select photos",
            "Select a folder");
        if (source is null || source == "Cancel")
        {
            return;
        }

        var files = new List<(string FileName, Func<Task<Stream>> Open)>();
        try
        {
            if (source == "Select photos")
            {
                foreach (var photo in await GalleryPhotos.PickAsync(multiple: true))
                {
                    var bytes = photo.Bytes;
                    files.Add((photo.FileName, () => Task.FromResult<Stream>(new MemoryStream(bytes))));
                }
            }
            else if (source == "Select a folder")
            {
                foreach (var item in await FolderImagePicker.PickAsync())
                {
                    var path = item.Uri.StartsWith("file://", StringComparison.Ordinal)
                        ? Uri.UnescapeDataString(item.Uri["file://".Length..])
                        : item.Uri;
                    files.Add((item.FileName, () => Task.FromResult<Stream>(File.OpenRead(path))));
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import", ex.Message, "OK");
            return;
        }

        files = files.Take(MaxPhotos).ToList();
        if (files.Count == 0)
        {
            return;
        }

        _rows.Children.Clear();
        _pick.IsEnabled = false;
        _busy.IsVisible = true;
        _busy.IsRunning = true;

        var enrolled = 0;
        var skipped = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            _summary.Text = $"Enrolling {i + 1} of {files.Count}…";
            byte[] jpeg;
            try
            {
                await using var stream = await file.Open();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                jpeg = GalleryImagePrep.PrepareEngineJpeg(memory.ToArray());
            }
            catch (Exception ex)
            {
                skipped++;
                _rows.Children.Add(ResultRow(file.FileName, ex.Message, false, null, null));
                continue;
            }

            // The file name is the name, so a batch is recognisable afterwards; a blank one
            // still falls back to "Person N" inside the gallery.
            var name = Path.GetFileNameWithoutExtension(file.FileName) ?? "";
            var outcome = await FaceGallery.EnrollAsync(name, "", jpeg, DemoSettings.Limits);
            if (outcome is EnrollOutcome.Enrolled done)
            {
                enrolled++;
                var detail = ShellUi.IdTag(done.Record.Id);
                if (done.Duplicates.Count > 0)
                {
                    detail += " · looks like " + string.Join(
                        ", ",
                        done.Duplicates.Select(c => $"{c.Name} {ShellUi.Score(c.Similarity)}"));
                }
                _rows.Children.Add(ResultRow(
                    done.Record.Name,
                    detail,
                    true,
                    done.Report,
                    FaceSource(done.Record.CropPath, done.Record.OriginalPath, jpeg)));
            }
            else if (outcome is EnrollOutcome.Rejected bad)
            {
                skipped++;
                _rows.Children.Add(ResultRow(file.FileName, bad.Reason, false, bad.Report, FaceSource(null, null, jpeg)));
            }
        }

        _busy.IsVisible = false;
        _busy.IsRunning = false;
        _pick.IsEnabled = true;
        _summary.Text = $"{enrolled} enrolled · {skipped} skipped";
    }

    /// <summary>The stored crop when enroll saved one, otherwise the photo that was read.</summary>
    static ImageSource? FaceSource(string? cropPath, string? originalPath, byte[]? jpeg)
    {
        foreach (var path in new[] { cropPath, originalPath })
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return ImageSource.FromFile(path);
            }
        }

        return jpeg is { Length: > 0 }
            ? ImageSource.FromStream(() => new MemoryStream(jpeg))
            : null;
    }

    View ResultRow(string title, string detail, bool ok, CallReport? report, ImageSource? face)
    {
        var photo = new Image
        {
            Aspect = Aspect.AspectFill,
            BackgroundColor = ShellUi.Card,
            Source = face,
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
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            WidthRequest = 56,
            HeightRequest = 56,
            BackgroundColor = ShellUi.Card,
        });
        grid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = title, FontSize = 15, TextColor = DemoTheme.Text },
                new Label { Text = detail, FontSize = 12, TextColor = DemoTheme.Muted },
            },
        }, 1);
        var pill = ShellUi.Pill(ok ? "Enrolled" : "Skipped", ok ? ShellUi.StatusMatch : ShellUi.Danger);
        pill.VerticalOptions = LayoutOptions.Center;
        grid.Add(pill, 2);

        var card = ShellUi.CardView(grid);
        if (report != null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await Navigation.PushModalAsync(new StatusPage("Enrollment status", null, report));
            card.GestureRecognizers.Add(tap);
        }
        return card;
    }
}
