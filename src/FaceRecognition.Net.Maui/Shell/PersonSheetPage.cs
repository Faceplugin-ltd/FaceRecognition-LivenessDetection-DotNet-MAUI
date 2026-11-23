namespace FaceRecognition.Net.Maui;

/// <summary>
/// One enrolled person: the stored photo, an editable name and note, when they were enrolled,
/// and the status card from the enrolment that created them. Android `shell/FaceDetailSheet.kt`.
/// </summary>
public class PersonSheetPage : ContentPage
{
    readonly int _id;
    readonly Entry _name = new() { TextColor = DemoTheme.Text, BackgroundColor = ShellUi.Card };
    readonly Entry _note = new() { TextColor = DemoTheme.Text, BackgroundColor = ShellUi.Card };

    public PersonSheetPage(int id)
    {
        _id = id;
        Title = "Person";
        BackgroundColor = DemoTheme.Bg;

        var record = FaceGallery.Get(id);
        if (record == null)
        {
            var missing = ShellUi.Header("Person", ShellUi.Back(this));
            missing.Padding = new Thickness(4, 4, 16, 0);
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    missing,
                    new Label
                    {
                        Text = "This person is no longer in the gallery.",
                        TextColor = DemoTheme.Muted,
                        Margin = new Thickness(16),
                    },
                },
            };
            return;
        }

        _name.Text = record.Name;
        _note.Text = record.Note;

        var photo = new Image
        {
            HeightRequest = 220,
            Aspect = Aspect.AspectFit,
            BackgroundColor = ShellUi.Card,
        };
        var path = record.CropPath.Length > 0 ? record.CropPath : record.OriginalPath;
        if (path.Length > 0 && File.Exists(path))
        {
            photo.Source = ImageSource.FromFile(path);
        }

        var save = new Button
        {
            Text = "Save",
            TextColor = Colors.White,
            BackgroundColor = DemoTheme.Tile,
            CornerRadius = 20,
        };
        save.Clicked += async (_, _) =>
        {
            FaceGallery.Update(_id, _name.Text ?? "", _note.Text ?? "");
            await Navigation.PopModalAsync();
        };

        var remove = new HorizontalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Icons.Glyph("delete", ShellUi.Danger, 20),
                new Label
                {
                    Text = "Delete",
                    TextColor = ShellUi.Danger,
                    FontSize = 16,
                    VerticalOptions = LayoutOptions.Center,
                },
            },
        };
        var removeTap = new TapGestureRecognizer();
        removeTap.Tapped += async (_, _) =>
        {
            var ok = await DisplayAlert("Delete", $"Remove {record.Name}?", "Delete", "Cancel");
            if (!ok)
            {
                return;
            }
            FaceGallery.Delete(_id);
            await Navigation.PopModalAsync();
        };
        remove.GestureRecognizers.Add(removeTap);

        var head = ShellUi.Header(record.Name, ShellUi.Back(this));
        head.Padding = new Thickness(4, 4, 16, 0);

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
            Children =
            {
                photo,
                ShellUi.Section(
                    "Enrollment",
                    ShellUi.Row("ID", ShellUi.IdTag(record.Id)),
                    ShellUi.Row("Enrolled", ShellUi.EnrolledDateTime(record.CreatedAt)),
                    ShellUi.Row("Template", $"{Base64Bytes(record.Feature)} bytes")),
                new Label { Text = "Name", FontSize = 13, TextColor = DemoTheme.Muted },
                _name,
                new Label { Text = "Note", FontSize = 13, TextColor = DemoTheme.Muted },
                _note,
                new Grid
                {
                    ColumnSpacing = 12,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star),
                    },
                    Children = { remove, save },
                },
            },
        };
        Grid.SetColumn(save, 1);

        var report = record.Report;
        if (report != null)
        {
            body.Children.Add(ShellUi.StatusCard(report, "Enrollment status"));
        }

        var scroll = new ScrollView { Content = body };
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children = { head, scroll },
        };
        Grid.SetRow(scroll, 1);
    }

    static int Base64Bytes(string b64)
    {
        if (string.IsNullOrEmpty(b64))
        {
            return 0;
        }
        var padding = b64.EndsWith("==", StringComparison.Ordinal) ? 2
            : b64.EndsWith("=", StringComparison.Ordinal) ? 1
            : 0;
        return Math.Max(0, (b64.Length * 3 / 4) - padding);
    }
}
