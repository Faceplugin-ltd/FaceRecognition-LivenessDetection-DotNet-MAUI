namespace FaceRecognition.Net.Maui;

/// <summary>
/// The enrolled gallery: a search box, the count, and a row per person with the stored crop.
/// The header menu is the two ways in — a camera capture or a batch of photos — and the
/// overflow clears the gallery. Android `shell/PeopleFragment.kt`.
/// </summary>
public class PeoplePage : ContentPage
{
    const int PageSize = 30;

    readonly SearchBar _search = new()
    {
        Placeholder = "Search name or note",
        TextColor = DemoTheme.Text,
        PlaceholderColor = DemoTheme.Muted,
        BackgroundColor = ShellUi.Card,
    };

    readonly Label _count = new()
    {
        FontSize = 13,
        TextColor = DemoTheme.Muted,
        VerticalOptions = LayoutOptions.Center,
    };

    readonly VerticalStackLayout _list = new() { Spacing = 8 };
    readonly VerticalStackLayout _empty = new()
    {
        Spacing = 6,
        IsVisible = false,
        Padding = new Thickness(0, 48, 0, 0),
    };

    int _shown = PageSize;

    public PeoplePage()
    {
        Title = "People";
        BackgroundColor = DemoTheme.Bg;

        _empty.Add(new Label
        {
            Text = "No one enrolled yet",
            FontSize = 17,
            TextColor = DemoTheme.Text,
            HorizontalTextAlignment = TextAlignment.Center,
        });
        _empty.Add(new Label
        {
            Text = "Enroll a face with the camera, or bring in a batch of photos.",
            FontSize = 13,
            TextColor = DemoTheme.Muted,
            HorizontalTextAlignment = TextAlignment.Center,
        });

        var add = Icons.Button("personAdd", DemoTheme.Text, async () => await AddAsync());
        var clear = Icons.Button("delete", DemoTheme.Text, async () =>
        {
            var choice = await DisplayActionSheet("People", "Cancel", "Delete all");
            if (choice == "Delete all")
            {
                await DeleteAllAsync();
            }
        });

        var header = ShellUi.Header("People", null, _count, add, clear);
        header.Padding = new Thickness(12, 4, 8, 0);

        _search.TextChanged += (_, _) =>
        {
            _shown = PageSize;
            Refresh();
        };

        var more = new Button
        {
            Text = "Show more",
            TextColor = DemoTheme.Accent,
            BackgroundColor = Colors.Transparent,
        };
        more.Clicked += (_, _) =>
        {
            _shown += PageSize;
            Refresh();
        };
        _more = more;

        var list = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(16, 8, 16, 24),
                Children = { _list, _empty, _more },
            },
        };
        var search = new ContentView { Content = _search, Padding = new Thickness(12, 8, 12, 0) };
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children = { header, search, list },
        };
        Grid.SetRow(search, 1);
        Grid.SetRow(list, 2);
    }

    readonly Button _more;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        FaceGallery.Changed += Refresh;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        FaceGallery.Changed -= Refresh;
    }

    void Refresh()
    {
        var query = _search.Text ?? "";
        var total = FaceGallery.Count();
        var matching = FaceGallery.Count(query);
        var rows = FaceGallery.Page(query, _shown, 0);

        _count.Text = query.Length == 0
            ? $"{total} enrolled"
            : $"{matching} of {total}";
        _empty.IsVisible = rows.Count == 0;
        _more.IsVisible = matching > rows.Count;

        _list.Children.Clear();
        foreach (var record in rows)
        {
            _list.Children.Add(PersonRow(record));
        }
    }

    View PersonRow(FaceRecord record)
    {
        var photo = new Image
        {
            WidthRequest = 56,
            HeightRequest = 56,
            Aspect = Aspect.AspectFill,
            BackgroundColor = ShellUi.Card,
        };
        var path = record.CropPath.Length > 0 ? record.CropPath : record.OriginalPath;
        if (path.Length > 0 && File.Exists(path))
        {
            photo.Source = ImageSource.FromFile(path);
        }

        var name = new Label
        {
            Text = record.Name,
            FontSize = 16,
            TextColor = DemoTheme.Text,
        };
        var date = ShellUi.EnrolledDate(record.CreatedAt);
        var sub = new Label
        {
            // Android shows both when there is a note: "note · date".
            Text = record.Note.Length > 0 ? $"{record.Note} · {date}" : date,
            FontSize = 12,
            TextColor = DemoTheme.Muted,
        };

        var text = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { name, sub },
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
        });
        grid.Add(text, 1);
        grid.Add(new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                ShellUi.Pill(ShellUi.IdTag(record.Id)),
                Icons.Glyph("chevron", DemoTheme.Muted, 22),
            },
        }, 2);

        var card = ShellUi.CardView(grid);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushModalAsync(new PersonSheetPage(record.Id));
        card.GestureRecognizers.Add(tap);
        return card;
    }

    async Task AddAsync()
    {
        // The two names say what each one does, as they do on the other apps.
        var choice = await DisplayActionSheet(
            "Add people",
            "Cancel",
            null,
            "Enroll with camera",
            "Bulk enroll from photos");
        if (choice == "Enroll with camera")
        {
            await OpenAfterSheetAsync(new CapturePage());
        }
        else if (choice == "Bulk enroll from photos")
        {
            await OpenAfterSheetAsync(new ImportPage());
        }
    }

    /// <summary>
    /// The action sheet is still dismissing when this runs. Pushing on top of that dismiss
    /// is rejected on iOS and the app stops, which is what Bulk enroll was hitting: the page
    /// is cheap to build, so the push started before the sheet was gone.
    /// </summary>
    async Task OpenAfterSheetAsync(Page page)
    {
        await Task.Delay(400);
        try
        {
            await Navigation.PushAsync(page);
        }
        catch (Exception ex)
        {
            await DisplayAlert("People", ex.Message, "OK");
        }
    }

    async Task DeleteAllAsync()
    {
        if (FaceGallery.Count() == 0)
        {
            await DisplayAlert("People", "Nothing to delete.", "OK");
            return;
        }
        var ok = await DisplayAlert(
            "Delete all",
            "Remove every enrolled face and its photos? Enroll IDs restart at 1.",
            "Delete all",
            "Cancel");
        if (!ok)
        {
            return;
        }
        var removed = FaceGallery.DeleteAll();
        _shown = PageSize;
        await DisplayAlert("People", $"Deleted {removed}.", "OK");
    }
}
