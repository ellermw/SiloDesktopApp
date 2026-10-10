using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Views.Dialogs;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class ConditionalEditorsNativeFixture
{
    private static readonly List<string> Failures = [];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    internal static async Task RunAsync(StackPanel parent)
    {
        Failures.Clear();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var window = new Window { Content = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] } };
        var owner = (Grid)window.Content;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://editors-fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("fixture-only", "fixture-refresh-only", 86400); Admin(auth);
        var toast = new ToastService(); var toasts = new ToastContainer(); owner.Children.Add(toasts); toast.Register(toasts, owner.DispatcherQueue);
        using var services = new ServiceCollection().AddSingleton(auth).AddSingleton(new PeopleApi(client)).AddSingleton(new CatalogApi(client))
            .AddSingleton(new MediaMaintenanceApi(client)).AddSingleton(toast).BuildServiceProvider();
        field.SetValue(null, services);
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 900)); window.AppWindow.Show(false); await Task.Delay(160);
            await CaseAsync("editor wide capture and Cancel", () => CaptureEditorsAsync(owner, wire, "wide"));
            await CaseAsync("metadata series season episode field rows", () => VariantRowsAsync(owner, wire));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 740)); await Task.Delay(160);
            await CaseAsync("editor narrow capture and Cancel", () => CaptureEditorsAsync(owner, wire, "narrow"));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 900)); await Task.Delay(120);
            await CaseAsync("metadata unchanged save", () => UnchangedAsync(new EditMetadataDialog(Movie()), owner, wire));
            await CaseAsync("metadata tag entry duplicate removal and save", () => TagsAsync(owner, wire));
            await CaseAsync("person unchanged save", () => UnchangedAsync(new EditPersonDialog(Person()), owner, wire));
            await CaseAsync("metadata diff clear pending rejection retry", () => MetadataSaveAsync(owner, wire));
            await CaseAsync("person diff clear pending rejection retry", () => PersonSaveAsync(owner, wire));
            await CaseAsync("image immediate apply Cancel", () => ImageAsync(owner, wire, false));
            await CaseAsync("image current lock merge with metadata save", () => ImageAsync(owner, wire, true));
            await CaseAsync("image pending tab switch keeps submitted type", () => ImageTabSwitchAsync(owner, wire));
            foreach (var translated in new[] { 0, 1, 3 })
                await CaseAsync("translation result " + translated, () => TranslationAsync(owner, wire, translated));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(460, 740)); await Task.Delay(120);
            await CaseAsync("narrow translation visibility", () => TranslationVisibilityAsync(owner, wire));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 900)); await Task.Delay(100);
            await CaseAsync("permission revocation metadata/person/images", () => PermissionAsync(owner, wire, auth));
            if (Failures.Count > 0) throw new InvalidOperationException("Conditional editor native failures: " + string.Join(" | ", Failures));
            Program.Log("PASS conditional editor native wide/narrow, unchanged and Cancel no-write, diff-only/null, pending/rejection retry, immediate image/lock merge and revoked permissions");
        }
        finally { toast.Unregister(); field.SetValue(null, previous); window.Close(); }
    }
    private static void Admin(AuthService auth)
    {
        auth.SetCurrentUser(new() { Id = "fixture", Username = "Fixture", Role = "admin" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Primary", IsPrimary = true });
    }
    private static MediaItemDetail Movie() => new() { ContentId = "fixture-movie", Type = "movie", Title = "Fixture Movie", Overview = "An isolated movie overview.", Year = 2025, Runtime = 120, ReleaseDate = "2025-01-02", Genres = ["Drama"], LockedFields = [3] };
    private static Person Person() => new() { Id = "fixture-person", Name = "Fixture Person", Bio = "An isolated biography.", BirthDate = "1970-01-02", Birthplace = "Fixture City", Homepage = "https://fixture.invalid", TmdbId = "123" };
    private static async Task CaseAsync(string label, Func<Task> run)
    {
        try { await run(); Program.Log("PASS editor case " + label); }
        catch (Exception ex) { Failures.Add(label + ": " + ex.Message); Program.Log("FAIL editor case " + label + ": " + ex.Message); }
    }
    private static void Check(bool condition, string message) { if (!condition) { Failures.Add(message); Program.Log("FAIL " + message); } }
    private static async Task CaptureEditorsAsync(FrameworkElement owner, Wire wire, string size)
    {
        wire.Reset();
        var metadata = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = metadata.ShowAsync(); await Task.Delay(150);
        try
        {
            var expectedWidth = size == "wide" ? 1100 : 460;
            var expectedHeight = size == "wide" ? 900 : 740;
            Check(Math.Abs(owner.XamlRoot.Size.Width - expectedWidth) < 1 && Math.Abs(owner.XamlRoot.Size.Height - expectedHeight) < 1,
                "editor capture viewport does not match the requested client size");
            Program.Log("TRACE editor " + size + " viewport " + owner.XamlRoot.Size.Width + "x" + owner.XamlRoot.Size.Height);
            var texts = Descendants<TextBlock>(metadata).ToArray();
            Check(texts.Any(text => text.Text == "Movie" && Math.Abs(text.FontSize - 11) < .1), "metadata header type chip absent");
            Check(texts.Any(text => text.Text == "1 locked"), "metadata header locked count absent");
            var navigation = Field<StackPanel>(metadata, "_navigation");
            Check(navigation.Orientation == (size == "narrow" ? Orientation.Horizontal : Orientation.Vertical), "metadata navigation does not match viewport");
            Check(navigation.Children.OfType<Button>().First().BorderThickness == (size == "narrow" ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 2, 0)),
                "metadata active tab has no responsive selection edge");
            var tabs = navigation.Children.OfType<Button>().ToArray();
            if (size == "wide")
            {
                var firstTab = tabs[0].TransformToVisual(metadata).TransformPoint(new Windows.Foundation.Point());
                var secondTab = tabs[1].TransformToVisual(metadata).TransformPoint(new Windows.Foundation.Point());
                Check(Math.Abs(secondTab.Y - firstTab.Y - 36) < 1, "metadata tabs retain an extra inter-tab gap absent from the WebUI");
            }
            var fields = Field<Dictionary<string, TextBox>>(metadata, "_textInputs");
            if (size == "narrow") Check(Math.Abs(fields["title"].ActualWidth - 426) < 1,
                "narrow metadata fields lose32px to a duplicate outer horizontal gutter");
            var sort = fields["sort_title"].TransformToVisual(metadata).TransformPoint(new Windows.Foundation.Point());
            var original = fields["original_title"].TransformToVisual(metadata).TransformPoint(new Windows.Foundation.Point());
            Check(size == "wide" ? Math.Abs(sort.Y - original.Y) < 2 && original.X > sort.X : original.Y > sort.Y,
                "metadata sort/original title fields do not follow responsive pairs");
            Check(fields["overview"].ActualHeight == 100 && fields["title"].ActualHeight == 36, "metadata input/overview geometry differs from current editor");
            Check(fields.Values.All(input => input.CornerRadius.TopLeft == 10), "metadata fields override the actual theme-derived10px source corners");
            var primary = Descendants<Button>(metadata).Single(button => button.Name == "PrimaryButton");
            Check(size == "narrow" || primary.ActualWidth < 180, "wide metadata Save still expands across the footer");
            Program.Log("TRACE metadata " + size + " body=" + Field<Grid>(metadata, "_root").ActualWidth + "x" + Field<Grid>(metadata, "_root").ActualHeight);
            Check(metadata.CornerRadius.TopLeft == 12, "metadata dialog corner radius differs from current editor");
            await CaptureAsync(metadata, $"edit-metadata-{size}-general.png");
            Section(metadata, "Dates & Ratings"); await Task.Delay(80);
            Check(Descendants<TextBlock>(metadata).Any(text => text.Text == "RATINGS"), "metadata ratings section heading disappears before its fields are realized");
            await CaptureAsync(metadata, $"edit-metadata-{size}-dates.png");
            Section(metadata, "Tags & Genres"); await Task.Delay(80); metadata.UpdateLayout();
            var tags = Field<Dictionary<string, MetadataTagsInput>>(metadata, "_tagInputs")["genres"];
            var tagEntry = Descendants<TextBox>(tags).Single();
            var tagRemove = Descendants<Button>(tags).First();
            var entryTop = tagEntry.TransformToVisual(tags).TransformPoint(new Windows.Foundation.Point()).Y;
            var chipTop = tagRemove.TransformToVisual(tags).TransformPoint(new Windows.Foundation.Point()).Y;
            Check(Math.Abs(entryTop - chipTop) < 10 && tags.ActualHeight <= 40, "metadata tags and entry are split into separate rows instead of one outlined field");
            await CaptureAsync(metadata, $"edit-metadata-{size}-tags.png");
            Section(metadata, "External IDs"); await Task.Delay(80);
            var imdb = fields["imdb_id"].TransformToVisual(metadata).TransformPoint(new Windows.Foundation.Point());
            var tmdb = fields["tmdb_id"].TransformToVisual(metadata).TransformPoint(new Windows.Foundation.Point());
            Check(Math.Abs(imdb.X - tmdb.X) < 1 && tmdb.Y > imdb.Y && Math.Abs(fields["imdb_id"].ActualWidth - fields["tmdb_id"].ActualWidth) < 1,
                "metadata External IDs are not full-width stacked rows");
            Section(metadata, "Images"); await Task.Delay(100); metadata.UpdateLayout();
            var imageSection = Field<Dictionary<string, StackPanel>>(metadata, "_sections")["images"];
            var artwork = imageSection.Children.OfType<WrapPanel>().Single();
            var firstImage = Descendants<Image>(artwork.Children.OfType<Button>().First()).Single();
            Check(size != "wide" || Math.Abs(firstImage.ActualWidth - 192.5) < 1,
                "metadata poster widths wrap the fourth desktop image into another row");
            if (size == "wide")
            {
                var posterCards = artwork.Children.OfType<Button>().ToArray();
                Check(posterCards.Length == 4 && Math.Abs(posterCards[3].TransformToVisual(artwork).TransformPoint(new Windows.Foundation.Point()).Y - posterCards[0].TransformToVisual(artwork).TransformPoint(new Windows.Foundation.Point()).Y) < 1,
                    "four actual poster choices must stay on the first desktop row after native layout rounding");
            }
            Check(imageSection.Children.OfType<Button>().Single().Visibility == Visibility.Collapsed,
                "metadata image Apply action occupies space before an image is selected");
            await CaptureAsync(metadata, $"edit-metadata-{size}-images.png");
            Close(metadata); await showing;
            Check(wire.Writes.Count == 0, "metadata Cancel performed a write");
        }
        finally { metadata.Hide(); await showing; }
        var person = new EditPersonDialog(Person()) { XamlRoot = owner.XamlRoot }; showing = person.ShowAsync(); await Task.Delay(150);
        try
        {
            var inputs = Field<Dictionary<string, TextBox>>(person, "_inputs");
            Check(inputs.Count == 9 && inputs["bio"].MinHeight == 128, "person nine-field biography contract absent");
            Check(inputs.Values.All(input => input.CornerRadius.TopLeft == 10), "person fields override the actual theme-derived10px source corners");
            Check(Grid.GetColumnSpan((FrameworkElement)inputs["birth_date"].Parent) == (size == "narrow" ? 2 : 1), "person dates do not follow responsive columns");
            Check(person.CornerRadius.TopLeft == 12, "person dialog corner radius differs from current editor");
            if (size == "narrow")
            {
                var backdrop = Descendants<Border>(person).Single(border => border.Name == "BackgroundElement");
                Check(backdrop.ActualHeight <= 676 && backdrop.ActualHeight > 650, "person narrow shell exceeds its reference viewport margin");
                Check(Math.Abs(backdrop.ActualWidth - 428) < 1, "person narrow shell shrinks away its reference scrollbar reserve");
                Check(Math.Abs(inputs["name"].ActualWidth - 368) < 1, "person narrow fields do not reserve the reference scrollbar width");
            }
            var personSave = Descendants<Button>(person).Single(button => button.Name == "PrimaryButton");
            Check(size == "narrow" || personSave.ActualWidth < 180, "wide person Save still expands across the footer");
            await CaptureAsync(person, $"edit-person-{size}.png"); Close(person); await showing;
            Check(wire.Writes.Count == 0, "person Cancel performed a write");
        }
        finally { person.Hide(); await showing; }
    }
    private static async Task VariantRowsAsync(FrameworkElement owner, Wire wire)
    {
        foreach (var type in new[] { "series", "season", "episode" })
        {
            var dialog = new EditMetadataDialog(new MediaItemDetail { ContentId = "fixture-" + type, Type = type, Title = "Fixture " + type }) { XamlRoot = owner.XamlRoot };
            var showing = dialog.ShowAsync(); await Task.Delay(120);
            try
            {
                var groups = Field<Dictionary<string, FrameworkElement>>(dialog, "_fieldGroups");
                var content = groups["content_rating"].TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point());
                var companion = groups[type == "series" ? "status" : "season_number"].TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point());
                Check(Math.Abs(content.Y - companion.Y) < 2 && companion.X > content.X, type + " content-rating row companion differs from current editor");
                if (type == "episode")
                {
                    var number = groups["episode_number"].TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point());
                    var runtime = groups["runtime"].TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point());
                    Check(number.Y > content.Y && Math.Abs(number.Y - runtime.Y) < 2 && runtime.X > number.X,
                        "episode number/runtime does not use the subsequent paired row");
                }
                var sections = Field<Dictionary<string, StackPanel>>(dialog, "_sections");
                if (sections.ContainsKey("images")) { Section(dialog, "Images"); await Task.Delay(60); }
                var hints = sections.TryGetValue("images", out var images)
                    ? Descendants<TextBlock>(images).Where(text => text.Text.StartsWith("Only seeing one poster?", StringComparison.Ordinal)).ToArray() : [];
                Check(hints.Length == (type == "season" ? 1 : 0), "season artwork plugin-update notice must appear only for seasons");
                if (type == "season" && hints.Length == 1) Check(hints[0].FontSize == 11 && hints[0].TextWrapping == TextWrapping.Wrap, "season artwork notice must wrap at current source size");
                Close(dialog); await showing;
                Check(wire.Writes.Count == 0, "metadata variant inspection performed a write");
            }
            finally { dialog.Hide(); await showing; }
        }
    }

    private static async Task UnchangedAsync(ContentDialog dialog, FrameworkElement owner, Wire wire)
    {
        wire.Reset(); dialog.XamlRoot = owner.XamlRoot; var showing = dialog.ShowAsync(); await Task.Delay(140);
        try { Save(dialog); await UntilAsync(() => showing.Status != Windows.Foundation.AsyncStatus.Started); Check(wire.Writes.Count == 0, "unchanged editor saved fields"); }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task MetadataSaveAsync(FrameworkElement owner, Wire wire)
    {
        wire.Reset(); wire.PatchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(150);
        try
        {
            Box(dialog, "Title").Text = "Changed Movie"; Section(dialog, "Dates & Ratings"); Box(dialog, "Release Date").Text = "";
            Program.Log("TRACE metadata before Save draft=" + JsonSerializer.Serialize(Field<MetadataEditState>(dialog, "_state").Changes(Movie().LockedFields)));
            Save(dialog); await UntilAsync(() => wire.Writes.Count == 1, () => "metadata initial PATCH: writes=" + wire.Writes.Count + ", primary=" + dialog.IsPrimaryButtonEnabled + ", text=" + dialog.PrimaryButtonText + ", draft=" + JsonSerializer.Serialize(Field<MetadataEditState>(dialog, "_state").Changes(Movie().LockedFields)) + ", visible=" + showing.Status + ", notifications=" + string.Join(";", Descendants<TextBlock>(owner).Select(text => text.Text)));
            Program.Log("TRACE metadata first PATCH=" + wire.Writes[0].Body.GetRawText());
            Check(!dialog.IsPrimaryButtonEnabled && dialog.PrimaryButtonText == "Saving...", "metadata save remains enabled while pending");
            var payload = wire.Writes[0].Body;
            Check(payload.GetProperty("title").GetString() == "Changed Movie" && payload.GetProperty("release_date").ValueKind == JsonValueKind.Null, "metadata title/date payload incorrect");
            Check(payload.EnumerateObject().Select(value => value.Name).Order().SequenceEqual(new[] { "locked_fields", "release_date", "title" }), "metadata save includes unchanged fields");
            Check(payload.GetProperty("locked_fields").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(new[] { 0, 3, 13 }), "metadata save lost inherited or automatic locks");
            await CaptureAsync(dialog, "edit-metadata-pending.png");
            wire.PatchGate.SetResult(Response(HttpStatusCode.UnprocessableEntity, "{\"error\":\"validation_failed\",\"message\":\"Fixture rejection\"}"));
            await UntilAsync(() => dialog.IsPrimaryButtonEnabled, () => "metadata rejection: writes=" + wire.Writes.Count + ", primary=" + dialog.IsPrimaryButtonEnabled + ", text=" + dialog.PrimaryButtonText); Program.Log("TRACE metadata rejected and draft restored"); Check(showing.Status == Windows.Foundation.AsyncStatus.Started, "metadata rejection closed the dialog");
            await CaptureAsync(dialog, "edit-metadata-rejected.png"); wire.PatchGate = null; Save(dialog); await showing;
            Check(wire.Writes.Count == 2 && wire.Writes[1].Body.GetRawText() == payload.GetRawText() && dialog.HasSaved, "metadata retry discarded draft or reported failure");
        }
        finally { wire.PatchGate?.TrySetResult(Response(HttpStatusCode.OK, JsonSerializer.Serialize(Movie(), Json))); dialog.Hide(); await showing; }
    }
    private static async Task PersonSaveAsync(FrameworkElement owner, Wire wire)
    {
        wire.Reset(); wire.PatchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new EditPersonDialog(Person()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(140);
        try
        {
            var inputs = Field<Dictionary<string, TextBox>>(dialog, "_inputs"); inputs["name"].Text = "Changed Person"; inputs["birth_date"].Text = "";
            Save(dialog); await UntilAsync(() => wire.Writes.Count == 1);
            Check(!dialog.IsPrimaryButtonEnabled, "person save remains enabled while pending");
            var payload = wire.Writes[0].Body;
            Check(payload.EnumerateObject().Count() == 2 && payload.GetProperty("name").GetString() == "Changed Person" && payload.GetProperty("birth_date").ValueKind == JsonValueKind.Null, "person diff-only/null payload incorrect");
            await CaptureAsync(dialog, "edit-person-pending.png");
            wire.PatchGate.SetResult(Response(HttpStatusCode.UnprocessableEntity, "{\"error\":\"validation_failed\",\"message\":\"Fixture rejection\"}"));
            await UntilAsync(() => dialog.IsPrimaryButtonEnabled); Check(showing.Status == Windows.Foundation.AsyncStatus.Started, "person rejection closed editor");
            wire.PatchGate = null; Save(dialog); await showing;
            Check(wire.Writes.Count == 2 && wire.Writes[1].Body.GetRawText() == payload.GetRawText() && dialog.HasSaved, "person retry discarded draft");
        }
        finally { wire.PatchGate?.TrySetResult(Response(HttpStatusCode.OK, JsonSerializer.Serialize(Person(), Json))); dialog.Hide(); await showing; }
    }
    private static async Task ImageAsync(FrameworkElement owner, Wire wire, bool saveMetadata)
    {
        wire.Reset(); wire.ImageGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(150);
        try
        {
            Box(dialog, "Title").Text = "Image and Title"; Section(dialog, "Images"); await Task.Delay(100);
            var images = Field<Dictionary<string, StackPanel>>(dialog, "_sections")["images"];
            var grid = images.Children.OfType<WrapPanel>().Single(); Invoke(grid.Children.OfType<Button>().Last());
            var apply = images.Children.OfType<Button>().Single(); Invoke(apply); await UntilAsync(() => wire.Writes.Count == 1);
            Check(!dialog.IsPrimaryButtonEnabled && !apply.IsEnabled, "image apply did not inhibit metadata Save");
            Check(wire.Writes[0].Body.GetProperty("original_url").GetString() == "https://fixture.invalid/new-poster" && wire.Writes[0].Body.GetProperty("type").GetString() == "poster" && wire.Writes[0].Body.GetProperty("provider_id").GetString() == "fixture", "image apply request lost original/type/provider");
            await CaptureAsync(dialog, "edit-metadata-image-pending.png");
            wire.ImageGate.SetResult(Response(HttpStatusCode.OK, "{}")); await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
            Check(dialog.HasSaved && Descendants<TextBlock>(grid).Any(text => text.Text == "Current"), "applied image did not immediately update current state");
            if (saveMetadata)
            {
                Save(dialog); await showing; var payload = wire.Writes.Last().Body;
                Check(payload.GetProperty("title").GetString() == "Image and Title" && payload.GetProperty("locked_fields").EnumerateArray().Select(value => value.GetInt32()).SequenceEqual(new[] { 0, 3, 10 }), "metadata save did not merge current image lock");
            }
            else { Close(dialog); await showing; Check(wire.Writes.Count == 1, "Cancel wrote metadata or rolled back immediate image"); }
        }
        finally { wire.ImageGate?.TrySetResult(Response(HttpStatusCode.OK, "{}")); dialog.Hide(); await showing; }
    }
    private static async Task ImageTabSwitchAsync(FrameworkElement owner, Wire wire)
    {
        wire.Reset(); wire.ImageGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(150);
        try
        {
            Section(dialog, "Images");
            var images = Field<Dictionary<string, StackPanel>>(dialog, "_sections")["images"];
            var grid = images.Children.OfType<WrapPanel>().Single();
            await UntilAsync(() => grid.Children.OfType<Button>().Count() == 4);
            Invoke(grid.Children.OfType<Button>().Last());
            Invoke(images.Children.OfType<Button>().Single()); await UntilAsync(() => wire.Writes.Count == 1);
            var tabs = Descendants<StackPanel>(images).Single(panel => panel.Children.OfType<Button>().Any(button => Equals(button.Content, "Backdrops")));
            var backdrop = tabs.Children.OfType<Button>().Single(button => Equals(button.Content, "Backdrops"));
            Check(backdrop.IsEnabled, "source image tabs were disabled during apply"); Invoke(backdrop);
            await UntilAsync(() => grid.Children.OfType<Button>().Count() == 0);
            wire.ImageGate.SetResult(Response(HttpStatusCode.OK, "{}")); await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
            Invoke(tabs.Children.OfType<Button>().Single(button => Equals(button.Content, "Posters")));
            await UntilAsync(() => grid.Children.OfType<Button>().Count() == 4);
            dialog.UpdateLayout(); await Task.Delay(40);
            Check(wire.Writes[0].Body.GetProperty("type").GetString() == "poster", "pending image apply wire changed type");
            var correctCurrent = Descendants<TextBlock>(grid.Children.OfType<Button>().Last()).Any(text => text.Text == "Current")
                && !Descendants<TextBlock>(grid.Children.OfType<Button>().First()).Any(text => text.Text == "Current");
            Check(correctCurrent, "switching image tabs while apply was pending stored the submitted poster as a backdrop");
            Close(dialog); await showing; Check(wire.Writes.Count == 1, "tab switch wrote additional metadata");
            if (correctCurrent) Program.Log("PASS: real pending poster apply survives enabled Backdrops navigation; returning Posters marks the submitted poster Current without extra writes.");
        }
        finally { wire.ImageGate?.TrySetResult(Response(HttpStatusCode.OK, "{}")); dialog.Hide(); await showing; }
    }
    private static async Task TranslationAsync(FrameworkElement owner, Wire wire, int translated)
    {
        wire.Reset(); wire.TranslationTotal = translated;
        var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync();
        try
        {
            await UntilAsync(() => Descendants<Button>(dialog).Any(button => Equals(button.Content, "Translate")));
            var languages = Descendants<ComboBox>(dialog).Single(combo => Equals(combo.Header, "Language"));
            languages.SelectedItem = languages.Items.OfType<ComboBoxItem>().First();
            var button = Descendants<Button>(dialog).Single(button => Equals(button.Content, "Translate"));
            dialog.UpdateLayout();
            var translationY = button.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).Y;
            var runtime = Field<Dictionary<string, NumberBox>>(dialog, "_numberInputs")["runtime"];
            var runtimeY = runtime.TransformToVisual(dialog).TransformPoint(new Windows.Foundation.Point()).Y;
            Check(translationY < runtimeY, "translation controls are placed after runtime rather than before the content-rating row");
            var scroll = Field<ScrollViewer>(dialog, "_scroll");
            Check(scroll.ScrollableHeight < 1, "wide metadata translation form scrolls despite sufficient viewport space");
            var expected = translated == 0 ? "Nothing to translate — all descriptions are already localized."
                : $"Translated {translated} description{(translated == 1 ? "" : "s")}.";
            Invoke(button); await UntilAsync(() => wire.Writes.Count == 1 && button.IsEnabled && dialog.HasSaved);
            await UntilAsync(() => Descendants<TextBlock>(owner).Any(text => text.Text == expected),
                () => "actual translation completion did not report the precise result: " + expected);
            var payload = wire.Writes[0].Body;
            Check(payload.GetProperty("target_language").GetString() != "" && !payload.GetProperty("include_children").GetBoolean()
                && !payload.GetProperty("force").GetBoolean(), "translation request lost language/movie/force contract");
            Close(dialog); await showing; Check(wire.Writes.Count == 1, "Cancel wrote metadata after immediate translation");
        }
        finally { dialog.Hide(); await showing; wire.TranslationTotal = null; }
    }
    private static async Task TagsAsync(FrameworkElement owner, Wire wire)
    {
        wire.Reset(); var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(120);
        try
        {
            Section(dialog, "Tags & Genres"); await Task.Delay(60);
            var tags = Field<Dictionary<string, MetadataTagsInput>>(dialog, "_tagInputs")["genres"];
            var input = Descendants<TextBox>(tags).Single(); input.Text = "Comedy, Drama"; await Task.Delay(60);
            Check(tags.Values.SequenceEqual(new[] { "Drama", "Comedy" }), "comma tag entry loses input or duplicates an existing value");
            var remove = Descendants<Button>(tags).Single(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Remove Drama");
            Invoke(remove); await Task.Delay(60);
            Check(tags.Values.SequenceEqual(new[] { "Comedy" }) && Descendants<TextBox>(tags).Single().Text.Length == 0,
                "inline tag removal loses the remaining value or restores committed text");
            Save(dialog); await UntilAsync(() => wire.Writes.Count == 1 && dialog.HasSaved); await showing;
            Check(wire.Writes[0].Body.GetProperty("genres").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "Comedy" }),
                "tag changes do not reach the metadata diff write");
        }
        finally { dialog.Hide(); await showing; }
    }
    private static async Task TranslationVisibilityAsync(FrameworkElement owner, Wire wire)
    {
        wire.Reset(); wire.TranslationTotal = 0;
        var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync();
        try
        {
            await UntilAsync(() => Descendants<Button>(dialog).Any(button => Equals(button.Content, "Translate")));
            dialog.UpdateLayout(); await Task.Delay(60);
            var shell = Descendants<Border>(dialog).Single(border => border.Name == "BackgroundElement");
            var cancel = Descendants<Button>(dialog).Single(button => button.Name == "CloseButton");
            var shellTop = shell.TransformToVisual(owner).TransformPoint(new Windows.Foundation.Point()).Y;
            var cancelTop = cancel.TransformToVisual(owner).TransformPoint(new Windows.Foundation.Point()).Y;
            Check(shellTop >= 31 && shellTop + shell.ActualHeight <= owner.ActualHeight - 31,
                "narrow translation dialog escapes its viewport margins");
            Check(cancelTop >= 0 && cancelTop + cancel.ActualHeight <= owner.ActualHeight - 31,
                "narrow translation dialog hides its Cancel footer");
            var scroll = Field<ScrollViewer>(dialog, "_scroll");
            scroll.ChangeView(null, scroll.ScrollableHeight, null, true); await Task.Delay(80);
            var runtime = Field<Dictionary<string, NumberBox>>(dialog, "_numberInputs")["runtime"];
            var runtimeTop = runtime.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point()).Y;
            Check(runtimeTop >= 0 && runtimeTop + runtime.ActualHeight <= scroll.ActualHeight,
                "narrow translation form cannot scroll to its final runtime field");
            await CaptureAsync(dialog, "edit-metadata-narrow-translation-bottom.png");
            Close(dialog); await showing; Check(wire.Writes.Count == 0, "translation inspection Cancel performed a write");
        }
        finally { dialog.Hide(); await showing; wire.TranslationTotal = null; }
    }
    private static async Task PermissionAsync(FrameworkElement owner, Wire wire, AuthService auth)
    {
        wire.Reset(); var dialog = new EditMetadataDialog(Movie()) { XamlRoot = owner.XamlRoot }; var showing = dialog.ShowAsync(); await Task.Delay(150);
        try
        {
            Box(dialog, "Title").Text = "Must not save"; auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); Save(dialog); await Task.Delay(100);
            Check(wire.Writes.Count == 0 && showing.Status == Windows.Foundation.AsyncStatus.Started, "metadata save ignored permission revocation");
            Admin(auth); Section(dialog, "Images"); await Task.Delay(100); var images = Field<Dictionary<string, StackPanel>>(dialog, "_sections")["images"];
            Invoke(images.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Last()); auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); Invoke(images.Children.OfType<Button>().Single()); await Task.Delay(100);
            Check(wire.Writes.Count == 0, "image apply ignored revoked admin permission");
        }
        finally { dialog.Hide(); await showing; Admin(auth); }
        var person = new EditPersonDialog(Person()) { XamlRoot = owner.XamlRoot }; showing = person.ShowAsync(); await Task.Delay(140);
        try { Field<Dictionary<string, TextBox>>(person, "_inputs")["name"].Text = "Must not save"; auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); Save(person); await Task.Delay(100); Check(wire.Writes.Count == 0, "person save ignored revoked admin permission"); }
        finally { person.Hide(); await showing; Admin(auth); }
    }
    private static void Section(EditMetadataDialog dialog, string label) => Invoke(Field<StackPanel>(dialog, "_navigation").Children.OfType<Button>().Single(button => Equals(button.Content, label)));
    private static TextBox Box(EditMetadataDialog dialog, string label)
    {
        var key = label switch { "Title" => "title", "Release Date" => "release_date", _ => throw new ArgumentOutOfRangeException(nameof(label)) };
        return Field<Dictionary<string, TextBox>>(dialog, "_textInputs")[key];
    }

    private static void Save(ContentDialog dialog) => Invoke(Descendants<Button>(dialog).First(button => button.Name == "PrimaryButton"));
    private static void Close(ContentDialog dialog) => Invoke(Descendants<Button>(dialog).First(button => button.Name == "CloseButton"));
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static async Task UntilAsync(Func<bool> ready, Func<string>? detail = null) { for (var attempt = 0; attempt < 150; attempt++) { if (ready()) return; await Task.Delay(20); } throw new TimeoutException(detail?.Invoke() ?? "Editor fixture did not settle"); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { if (root is T match) yield return match; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return item; }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(60); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new InvalidOperationException("Editor capture has no rendered pixels");
        var pixels = await bitmap.GetPixelsAsync(); var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray()); await encoder.FlushAsync();
    }
    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json) };
    private sealed record Write(string Path, JsonElement Body);
    private sealed class Wire : HttpMessageHandler
    {
        private readonly List<Write> _writes = []; private bool _applied;
        public IReadOnlyList<Write> Writes { get { lock (_writes) return _writes.ToArray(); } }
        public TaskCompletionSource<HttpResponseMessage>? PatchGate, ImageGate;
        public int? TranslationTotal;
        public void Reset() { lock (_writes) _writes.Clear(); _applied = false; PatchGate = ImageGate = null; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "editors-fixture.invalid") throw new InvalidOperationException("Fixture attempted external networking");
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Patch || request.Method == HttpMethod.Post)
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); lock (_writes) _writes.Add(new(path, document.RootElement.Clone()));
                if (path.EndsWith("/metadata-translation")) return Response(HttpStatusCode.OK, "{\"id\":\"fixture-translation\",\"status\":\"queued\"}");
                if (path.EndsWith("/images/apply")) { _applied = true; if (ImageGate != null) return await ImageGate.Task.WaitAsync(ct); return Response(HttpStatusCode.OK, "{}"); }
                if (request.Method == HttpMethod.Patch && PatchGate != null) return await PatchGate.Task.WaitAsync(ct);
            }
            if (path.EndsWith("/images")) return Response(HttpStatusCode.OK, """{"items":[{"type":"poster","url":"","original_url":"https://fixture.invalid/old-poster","provider_id":"fixture","language":"en"},{"type":"poster","url":"","original_url":"https://fixture.invalid/alternate-poster-1","provider_id":"fixture","language":"en"},{"type":"poster","url":"","original_url":"https://fixture.invalid/alternate-poster-2","provider_id":"fixture","language":"en"},{"type":"poster","url":"","original_url":"https://fixture.invalid/new-poster","provider_id":"fixture","language":"en"}],"current":{"poster_url":"https://fixture.invalid/old-poster"},"provider_errors":{},"page":{"has_more":false,"next_cursor":""}}""");
            if (path.EndsWith("/metadata-translation/jobs")) return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { jobs = new[] { new { id = "fixture-translation", status = "completed", fields_total = TranslationTotal ?? 0, fields_done = TranslationTotal ?? 0 } } }, Json));
            if (path.Contains("metadata-ai")) return Response(HttpStatusCode.OK, TranslationTotal == null ? "{\"state\":\"disabled\"}" : "{\"state\":\"available\"}");
            if (path.Contains("/people/")) return Response(HttpStatusCode.OK, JsonSerializer.Serialize(Person(), Json));
            var movie = Movie(); if (_applied) movie.LockedFields = [3, 10];
            return Response(HttpStatusCode.OK, JsonSerializer.Serialize(movie, Json));
        }
    }
}
