using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TidalUi3.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace TidalUi3.Pages;

// ── Models ────────────────────────────────────────────────────────────────────

public class DebugHistoryEntry
{
    public string Method    { get; init; } = "GET";
    public string Url       { get; init; } = "";
    public int    StatusCode{ get; init; }
    public long   ElapsedMs { get; init; }
    public string Body      { get; init; } = "";

    public string DisplayUrl  => Url.Length > 60 ? Url[..57] + "…" : Url;
    public string StatusText  => StatusCode.ToString();
    public string ElapsedText => $"{ElapsedMs} ms";

    public SolidColorBrush StatusBrush => StatusCode is >= 200 and < 300
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 222, 128))
        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113));
}

public class JsonTreeItem
{
    public string Label { get; set; } = "";
    public string Path  { get; set; } = "";
    public override string ToString() => Label;
}

public record SavedEndpoint
{
    public string Name  { get; init; } = "";
    public string Url   { get; init; } = "";
    public string Notes { get; init; } = "";
}

// ── Page ──────────────────────────────────────────────────────────────────────

public sealed partial class DebugPage : Page
{
    // ── Predefined endpoint catalog ───────────────────────────────────────────

    private record CatalogEntry(string Group, string Name, string UrlTemplate);

    private static readonly List<CatalogEntry> _catalog =
    [
        new("User",      "Get profile",         "https://api.tidalhifi.com/v1/users/{userId}?countryCode={cc}"),

        new("Search",    "Search all",           "https://api.tidalhifi.com/v1/search?query=test&types=ARTISTS,ALBUMS,TRACKS,PLAYLISTS&limit=10&countryCode={cc}"),
        new("Search",    "Top hits",             "https://api.tidalhifi.com/v1/search/top-hits?query=test&limit=5&countryCode={cc}"),

        new("Tracks",    "Get track",            "https://api.tidalhifi.com/v1/tracks/59978879?countryCode={cc}"),
        new("Tracks",    "Playback info",        "https://api.tidalhifi.com/v1/tracks/59978879/playbackinfo?audioquality=HI_RES_LOSSLESS&playbackmode=STREAM&assetpresentation=FULL"),
        new("Tracks",    "Track radio",          "https://api.tidalhifi.com/v1/tracks/59978879/radio?limit=10&countryCode={cc}"),

        new("Albums",    "Get album",            "https://api.tidalhifi.com/v1/albums/17927863?countryCode={cc}"),
        new("Albums",    "Album tracks",         "https://api.tidalhifi.com/v1/albums/17927863/tracks?limit=50&countryCode={cc}"),

        new("Artists",   "Get artist",           "https://api.tidalhifi.com/v1/artists/7804?countryCode={cc}"),
        new("Artists",   "Top tracks",           "https://api.tidalhifi.com/v1/artists/7804/toptracks?limit=10&countryCode={cc}"),
        new("Artists",   "Albums",               "https://api.tidalhifi.com/v1/artists/7804/albums?limit=20&countryCode={cc}"),
        new("Artists",   "Singles / EPs",        "https://api.tidalhifi.com/v1/artists/7804/albums?filter=EPSANDSINGLES&limit=20&countryCode={cc}"),
        new("Artists",   "Appears on",           "https://api.tidalhifi.com/v1/artists/7804/albums?filter=COMPILATIONS&limit=20&countryCode={cc}"),
        new("Artists",   "Bio",                  "https://api.tidalhifi.com/v1/artists/7804/bio?countryCode={cc}"),

        new("Playlists", "Get playlist",         "https://api.tidalhifi.com/v1/playlists/{playlist-uuid}?countryCode={cc}"),
        new("Playlists", "Playlist items",       "https://api.tidalhifi.com/v1/playlists/{playlist-uuid}/items?limit=50&countryCode={cc}"),

        new("Favorites", "Favorite tracks",      "https://api.tidalhifi.com/v1/users/{userId}/favorites/tracks?limit=50&countryCode={cc}"),
        new("Favorites", "Favorite albums",      "https://api.tidalhifi.com/v1/users/{userId}/favorites/albums?limit=50&countryCode={cc}"),
        new("Favorites", "Favorite playlists",   "https://api.tidalhifi.com/v1/users/{userId}/playlists?limit=50&countryCode={cc}"),

        new("Pages",     "For You",              "https://api.tidalhifi.com/v1/pages/for_you?countryCode={cc}&deviceType=BROWSER"),
        new("Pages",     "Mix",                  "https://api.tidalhifi.com/v1/pages/mix?mixId={mix-id}&countryCode={cc}&deviceType=BROWSER"),

        new("Lyrics",    "Track lyrics",         "https://triton.squid.wtf/lyrics/?id=59978879"),
    ];

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<DebugHistoryEntry> _history = new();
    private readonly List<SavedEndpoint> _savedEndpoints = new();
    private readonly List<(string Path, string Display)> _flatTreeItems = new();
    private string _currentRawJson = "";
    private bool _isTreeMode;
    private bool _isCompareMode;
    private bool _suppressSelectionChanged;

    // ── Init ──────────────────────────────────────────────────────────────────

    public DebugPage()
    {
        InitializeComponent();

        TokenBox.Text   = App.ApiClient.AccessToken ?? "(no token)";
        UserInfoText.Text = $"User ID: {App.ApiClient.UserId}  ·  Country: {App.ApiClient.CountryCode}";

        HistoryList.ItemsSource = _history;

        LoadSavedEndpoints();
        RebuildEndpointCombo();
    }

    // ── Endpoint combo ────────────────────────────────────────────────────────

    private void RebuildEndpointCombo()
    {
        _suppressSelectionChanged = true;
        EndpointCombo.Items.Clear();

        // Saved endpoints section
        if (_savedEndpoints.Count > 0)
        {
            AddGroupHeader("Saved");
            foreach (var ep in _savedEndpoints)
                AddEndpointItem($"  {ep.Name}", ep.Url, ep.Notes);
            EndpointCombo.Items.Add(new ComboBoxItem
            {
                IsEnabled = false,
                Height = 1,
                Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            });
        }

        // Built-in catalog
        string? lastGroup = null;
        foreach (var ep in _catalog)
        {
            if (ep.Group != lastGroup) { AddGroupHeader(ep.Group); lastGroup = ep.Group; }
            AddEndpointItem($"  {ep.Name}", SubstituteParams(ep.UrlTemplate), null);
        }

        _suppressSelectionChanged = false;
    }

    private void AddGroupHeader(string text)
    {
        EndpointCombo.Items.Add(new ComboBoxItem
        {
            Content = text,
            IsEnabled = false,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Padding = new Thickness(12, 8, 0, 2),
        });
    }

    private void AddEndpointItem(string label, string url, string? tooltip)
    {
        var item = new ComboBoxItem
        {
            Content = label,
            Tag = url,
            Padding = new Thickness(12, 3, 12, 3),
        };
        if (tooltip != null) ToolTipService.SetToolTip(item, tooltip);
        EndpointCombo.Items.Add(item);
    }

    private string SubstituteParams(string template) => template
        .Replace("{userId}", App.ApiClient.UserId.ToString())
        .Replace("{cc}", App.ApiClient.CountryCode ?? "US");

    private void EndpointCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;
        if (EndpointCombo.SelectedItem is ComboBoxItem { Tag: string url })
            UrlBox.Text = url;
    }

    // ── Method selector ───────────────────────────────────────────────────────

    private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BodyExpander is null) return;
        var method = (MethodCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        BodyExpander.Visibility = method == "GET" ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Send (normal mode) ────────────────────────────────────────────────────

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) _ = SendAsync();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e) => _ = SendAsync();

    private async Task SendAsync()
    {
        var url = UrlBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        SendButton.IsEnabled = false;
        RequestProgress.IsActive = true;
        StatusText.Text = "Sending…";
        ResponseText.Text = "";

        var method   = GetSelectedMethod();
        var headers  = ParseHeaders(HeadersBox.Text);
        var body     = method != HttpMethod.Get ? BodyBox.Text?.Trim() : null;
        var ct       = (ContentTypeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString();

        var sw = Stopwatch.StartNew();
        try
        {
            var (rawBody, statusCode) = await App.ApiClient.DebugRequestAsync(method, url, headers, body, ct);
            sw.Stop();

            _currentRawJson = rawBody;
            var formatted = TryFormatJson(rawBody);

            if (_isTreeMode)
                BuildTree(rawBody);
            else
                ResponseText.Text = formatted;

            var chars = rawBody.Length;
            StatusText.Text = $"{statusCode}  ·  {sw.ElapsedMilliseconds} ms  ·  {chars:N0} chars";
            StatusText.Foreground = statusCode is >= 200 and < 300
                ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                : (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

            PrependHistory(method.Method, url, statusCode, sw.ElapsedMilliseconds, formatted);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ResponseText.Text = ex.ToString();
            StatusText.Text = $"Error  ·  {sw.ElapsedMilliseconds} ms";
            StatusText.ClearValue(ForegroundProperty);
        }
        finally
        {
            SendButton.IsEnabled = true;
            RequestProgress.IsActive = false;
        }
    }

    // ── History ───────────────────────────────────────────────────────────────

    private void PrependHistory(string method, string url, int status, long ms, string formattedBody)
    {
        _history.Insert(0, new DebugHistoryEntry
        {
            Method     = method,
            Url        = url,
            StatusCode = status,
            ElapsedMs  = ms,
            Body       = formattedBody,
        });
        while (_history.Count > 50) _history.RemoveAt(_history.Count - 1);
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not DebugHistoryEntry entry) return;

        UrlBox.Text = entry.Url;
        _currentRawJson = entry.Body;

        if (_isTreeMode)
            BuildTree(entry.Body);
        else
            ResponseText.Text = entry.Body;

        StatusText.Text = $"{entry.StatusCode}  ·  {entry.ElapsedMs} ms  (from history)";
        StatusText.ClearValue(ForegroundProperty);

        // Allow re-selecting the same item next time
        HistoryList.SelectedItem = null;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e) => _history.Clear();

    // ── Tree view (Phase 2) ───────────────────────────────────────────────────

    private void RawModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isTreeMode = false;
        RawModeButton.IsChecked  = true;
        TreeModeButton.IsChecked = false;
        ResponseText.Visibility       = Visibility.Visible;
        TreeArea.Visibility           = Visibility.Collapsed;
        TreeSearchBox.Visibility      = Visibility.Collapsed;
        TreeSearchBox.Text            = "";
        TreeSearchResultsList.Visibility = Visibility.Collapsed;
    }

    private void TreeModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isTreeMode = true;
        TreeModeButton.IsChecked = true;
        RawModeButton.IsChecked  = false;
        ResponseText.Visibility  = Visibility.Collapsed;
        TreeArea.Visibility      = Visibility.Visible;
        TreeSearchBox.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(_currentRawJson))
            BuildTree(_currentRawJson);
    }

    private void BuildTree(string json)
    {
        ResponseTree.RootNodes.Clear();
        _flatTreeItems.Clear();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = BuildTreeNode("root", doc.RootElement, "root");
            root.IsExpanded = true;
            ResponseTree.RootNodes.Add(root);
        }
        catch
        {
            var err = new TreeViewNode { Content = new JsonTreeItem { Label = "(invalid JSON)", Path = "" } };
            ResponseTree.RootNodes.Add(err);
        }
    }

    private TreeViewNode BuildTreeNode(string key, JsonElement el, string path)
    {
        var node = new TreeViewNode { IsExpanded = false };

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                int count = 0;
                foreach (var _ in el.EnumerateObject()) count++;
                node.Content = new JsonTreeItem { Label = $"{key}  {{  {count} }}", Path = path };
                foreach (var prop in el.EnumerateObject())
                    node.Children.Add(BuildTreeNode(prop.Name, prop.Value, $"{path}.{prop.Name}"));
                break;
            }
            case JsonValueKind.Array:
            {
                int len = el.GetArrayLength();
                node.Content = new JsonTreeItem { Label = $"{key}  [  {len} ]", Path = path };
                for (int i = 0; i < len; i++)
                    node.Children.Add(BuildTreeNode($"[{i}]", el[i], $"{path}[{i}]"));
                break;
            }
            default:
            {
                var val = el.ToString();
                var label = $"{key}:  {val}";
                node.Content = new JsonTreeItem { Label = label, Path = path };
                _flatTreeItems.Add((path, label));
                break;
            }
        }
        return node;
    }

    private void ResponseTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not TreeViewNode { Content: JsonTreeItem item }) return;
        CopyText(item.Path);
        CopiedPathText.Text = $"Copied: {item.Path}";
        CopiedPathText.Visibility = Visibility.Visible;
    }

    private void TreeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TreeSearchBox.Text.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(query))
        {
            TreeSearchResultsList.Visibility = Visibility.Collapsed;
            ResponseTree.Visibility          = Visibility.Visible;
            return;
        }

        TreeSearchResultsList.Items.Clear();
        foreach (var (path, display) in _flatTreeItems)
            if (path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                display.Contains(query, StringComparison.OrdinalIgnoreCase))
                TreeSearchResultsList.Items.Add(display);

        TreeSearchResultsList.Visibility = Visibility.Visible;
        ResponseTree.Visibility          = Visibility.Collapsed;
    }

    private void TreeSearchResult_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TreeSearchResultsList.SelectedItem is string display)
        {
            // Find the path for the selected display string
            var match = _flatTreeItems.Find(x => x.Display == display);
            if (match.Path != null)
            {
                CopyText(match.Path);
                CopiedPathText.Text = $"Copied: {match.Path}";
                CopiedPathText.Visibility = Visibility.Visible;
            }
        }
    }

    // ── Compare mode (Phase 5) ────────────────────────────────────────────────

    private void CompareToggle_Click(object sender, RoutedEventArgs e)
    {
        _isCompareMode = CompareToggle.IsChecked == true;

        ResponseText.Visibility     = _isCompareMode ? Visibility.Collapsed : (_isTreeMode ? Visibility.Collapsed : Visibility.Visible);
        TreeArea.Visibility         = _isCompareMode ? Visibility.Collapsed : (_isTreeMode ? Visibility.Visible : Visibility.Collapsed);
        CompareGrid.Visibility      = _isCompareMode ? Visibility.Visible   : Visibility.Collapsed;
        ResponseToolbar.Visibility  = _isCompareMode ? Visibility.Collapsed : Visibility.Visible;
        HistoryPanel.Visibility     = _isCompareMode ? Visibility.Collapsed : Visibility.Visible;
        HistoryColumnDef.Width      = _isCompareMode ? new GridLength(0) : new GridLength(260);

        if (_isCompareMode && !string.IsNullOrEmpty(UrlBox.Text))
            UrlBoxA.Text = UrlBox.Text;
    }

    private void UseCurrentA_Click(object sender, RoutedEventArgs e) => UrlBoxA.Text = UrlBox.Text;
    private void UseCurrentB_Click(object sender, RoutedEventArgs e) => UrlBoxB.Text = UrlBox.Text;

    private void SendA_Click(object sender, RoutedEventArgs e) => _ = SendCompareAsync(UrlBoxA, StatusTextA, ResponseTextA, ProgressA);
    private void SendB_Click(object sender, RoutedEventArgs e) => _ = SendCompareAsync(UrlBoxB, StatusTextB, ResponseTextB, ProgressB);

    private async Task SendCompareAsync(TextBox urlBox, TextBlock statusBlock, TextBox responseBox, ProgressRing ring)
    {
        var url = urlBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        ring.IsActive = true;
        statusBlock.Text = "Sending…";
        responseBox.Text = "";

        var sw = Stopwatch.StartNew();
        try
        {
            var (rawBody, statusCode) = await App.ApiClient.DebugRequestAsync(GetSelectedMethod(), url);
            sw.Stop();
            responseBox.Text  = TryFormatJson(rawBody);
            statusBlock.Text  = $"{statusCode}  ·  {sw.ElapsedMilliseconds} ms  ·  {rawBody.Length:N0} chars";
            PrependHistory(GetSelectedMethod().Method, url, statusCode, sw.ElapsedMilliseconds, responseBox.Text);
        }
        catch (Exception ex)
        {
            sw.Stop();
            responseBox.Text = ex.ToString();
            statusBlock.Text = $"Error  ·  {sw.ElapsedMilliseconds} ms";
        }
        finally
        {
            ring.IsActive = false;
        }
    }

    private void SwapAB_Click(object sender, RoutedEventArgs e)
    {
        (UrlBoxA.Text, UrlBoxB.Text)           = (UrlBoxB.Text, UrlBoxA.Text);
        (ResponseTextA.Text, ResponseTextB.Text) = (ResponseTextB.Text, ResponseTextA.Text);
        (StatusTextA.Text, StatusTextB.Text)     = (StatusTextB.Text, StatusTextA.Text);
    }

    private void RunDiff_Click(object sender, RoutedEventArgs e)
    {
        var jsonA = ResponseTextA.Text?.Trim();
        var jsonB = ResponseTextB.Text?.Trim();

        if (string.IsNullOrEmpty(jsonA) || string.IsNullOrEmpty(jsonB))
        {
            DiffStatusText.Text = "Send both A and B first.";
            return;
        }

        try
        {
            using var docA = JsonDocument.Parse(jsonA);
            using var docB = JsonDocument.Parse(jsonB);
            var diffs = JsonDiff.Compute(docA.RootElement, docB.RootElement);

            if (diffs.Count == 0)
            {
                DiffText.Text = "(no differences)";
                DiffStatusText.Text = "Identical";
            }
            else
            {
                var lines = new System.Text.StringBuilder();
                foreach (var d in diffs)
                    lines.AppendLine(d.Display);
                DiffText.Text = lines.ToString();
                DiffStatusText.Text = $"{diffs.Count} difference{(diffs.Count == 1 ? "" : "s")}";
            }

            DiffOutputRow.Height = new GridLength(180);
        }
        catch (JsonException ex)
        {
            DiffText.Text = $"JSON parse error: {ex.Message}";
            DiffStatusText.Text = "Error";
            DiffOutputRow.Height = new GridLength(180);
        }
    }

    // ── Saved endpoints (Phase 4) ─────────────────────────────────────────────

    private void ConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        var name = SaveNameBox.Text?.Trim();
        var url  = UrlBox.Text?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) return;

        _savedEndpoints.Add(new SavedEndpoint
        {
            Name  = name,
            Url   = url,
            Notes = SaveNotesBox.Text?.Trim() ?? "",
        });

        SaveNameBox.Text  = "";
        SaveNotesBox.Text = "";
        SaveFlyout.Hide();

        PersistSavedEndpoints();
        RebuildEndpointCombo();
    }

    private void ExportSaved_Click(object sender, RoutedEventArgs e)
    {
        if (_savedEndpoints.Count == 0)
        {
            StatusText.Text = "No saved endpoints to export.";
            return;
        }
        var json = JsonSerializer.Serialize(_savedEndpoints, new JsonSerializerOptions { WriteIndented = true });
        CopyText(json);
        StatusText.Text = $"Exported {_savedEndpoints.Count} endpoints to clipboard.";
    }

    private void LoadSavedEndpoints()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values["DebugSavedEndpoints"] is string json)
            {
                var list = JsonSerializer.Deserialize<List<SavedEndpoint>>(json);
                if (list != null) _savedEndpoints.AddRange(list);
            }
        }
        catch { }
    }

    private void PersistSavedEndpoints()
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values["DebugSavedEndpoints"] =
                JsonSerializer.Serialize(_savedEndpoints);
        }
        catch { }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        UrlBox.Text           = "";
        ResponseText.Text     = "";
        _currentRawJson       = "";
        StatusText.Text       = "";
        StatusText.ClearValue(ForegroundProperty);
        CopiedPathText.Visibility = Visibility.Collapsed;
        ResponseTree.RootNodes.Clear();
        _flatTreeItems.Clear();
        EndpointCombo.SelectedIndex = -1;
    }

    // ── Token ─────────────────────────────────────────────────────────────────

    private void CopyTokenButton_Click(object sender, RoutedEventArgs e) => CopyText(TokenBox.Text);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpMethod GetSelectedMethod()
    {
        var s = (MethodCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "GET";
        return s switch { "POST" => HttpMethod.Post, "DELETE" => HttpMethod.Delete, _ => HttpMethod.Get };
    }

    private static List<(string Key, string Value)> ParseHeaders(string? text)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            result.Add((line[..idx].Trim(), line[(idx + 1)..].Trim()));
        }
        return result;
    }

    private static string TryFormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private static void CopyText(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
    }
}
