using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Helpers;
using TidalUi3.Models;
using TidalUi3.Services;

namespace TidalUi3.Controls
{
    public sealed partial class PageHeaderControl : UserControl
    {
        public static readonly DependencyProperty SearchPlaceholderProperty =
            DependencyProperty.Register("SearchPlaceholder", typeof(string), typeof(PageHeaderControl),
                new PropertyMetadata("Search"));

        public static readonly DependencyProperty CanGoBackProperty =
            DependencyProperty.Register("CanGoBack", typeof(bool), typeof(PageHeaderControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty CanGoForwardProperty =
            DependencyProperty.Register("CanGoForward", typeof(bool), typeof(PageHeaderControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ApiClientProperty =
            DependencyProperty.Register("ApiClient", typeof(TidalApiClient), typeof(PageHeaderControl),
                new PropertyMetadata(null));

        public string SearchPlaceholder
        {
            get => (string)GetValue(SearchPlaceholderProperty);
            set => SetValue(SearchPlaceholderProperty, value);
        }

        public bool CanGoBack
        {
            get => (bool)GetValue(CanGoBackProperty);
            set => SetValue(CanGoBackProperty, value);
        }

        public bool CanGoForward
        {
            get => (bool)GetValue(CanGoForwardProperty);
            set => SetValue(CanGoForwardProperty, value);
        }

        public TidalApiClient? ApiClient
        {
            get => (TidalApiClient)GetValue(ApiClientProperty);
            set => SetValue(ApiClientProperty, value);
        }

        public event RoutedEventHandler? BackClick;
        public event RoutedEventHandler? ForwardClick;
        public event EventHandler<AutoSuggestBoxQuerySubmittedEventArgs>? SearchSubmitted;
        public event EventHandler<AutoSuggestBoxSuggestionChosenEventArgs>? SuggestionChosen;
        public event Action<Type, object>? NavigationRequested;

        private CancellationTokenSource? _searchCts;
        private Task? _searchDebounceTask;

        public PageHeaderControl()
        {
            this.InitializeComponent();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => BackClick?.Invoke(sender, e);
        private void ForwardButton_Click(object sender, RoutedEventArgs e) => ForwardClick?.Invoke(sender, e);

        private void HeaderSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;
            var query = sender.Text?.Trim() ?? "";

            if (query.Length < 2 || ApiClient?.IsAuthenticated != true)
            {
                sender.ItemsSource = null;
                return;
            }

            _searchDebounceTask = DebounceSearchAsync(sender, query, ct);
        }

        private async Task DebounceSearchAsync(AutoSuggestBox box, string query, CancellationToken ct)
        {
            try
            {
                await Task.Delay(300, ct);
                if (ct.IsCancellationRequested) return;

                var hits = await ApiClient!.SearchTopHitsAsync(query, limit: 3, ct: ct);
                if (ct.IsCancellationRequested) return;

                var suggestions = new ObservableCollection<SearchSuggestion>();

                // Text-only query suggestions (search icon)
                suggestions.Add(new SearchSuggestion
                {
                    Type = "search",
                    Display = query,
                    Name = query,
                    Icon = Glyphs.Search,
                    IsRichResult = false
                });

                // Rich: Tracks
                if (hits.Tracks?.Items is { Count: > 0 } tracks)
                {
                    foreach (var t in tracks.Take(3))
                        suggestions.Add(new SearchSuggestion
                        {
                            Type = "track",
                            IsRichResult = true,
                            Display = t.Title,
                            Subtitle = $"Song · {t.Artist?.Name ?? t.Artists.FirstOrDefault()?.Name ?? ""}",
                            CoverUrl = TidalApiClient.GetImageUrl(t.Album?.Cover, 80, 80) ?? "",
                            IsExplicit = t.Explicit,
                            TrackId = t.Id,
                            Name = t.Title,
                            Id = t.Album?.Id ?? 0,
                            ArtistId = t.Artist?.Id ?? t.Artists.FirstOrDefault()?.Id ?? 0
                        });
                }

                // Rich: Albums
                if (hits.Albums?.Items is { Count: > 0 } albums)
                {
                    foreach (var a in albums.Take(2))
                        suggestions.Add(new SearchSuggestion
                        {
                            Type = "album",
                            IsRichResult = true,
                            Display = a.Title,
                            Subtitle = $"Album · {a.Artist?.Name ?? a.Artists.FirstOrDefault()?.Name ?? ""}",
                            CoverUrl = TidalApiClient.GetImageUrl(a.Cover, 80, 80) ?? "",
                            Id = a.Id,
                            Name = a.Title
                        });
                }

                // Rich: Artists
                if (hits.Artists?.Items is { Count: > 0 } artists)
                {
                    foreach (var a in artists.Take(2))
                        suggestions.Add(new SearchSuggestion
                        {
                            Type = "artist",
                            IsRichResult = true,
                            Display = a.Name,
                            Subtitle = "Artist",
                            CoverUrl = TidalApiClient.GetImageUrl(a.Picture, 80, 80) ?? "",
                            Id = a.Id,
                            Name = a.Name
                        });
                }

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ct.IsCancellationRequested)
                        box.ItemsSource = suggestions;
                });
            }
            catch (OperationCanceledException) { }
            catch { /* swallow network errors during typing */ }
        }

        private void HeaderSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            SearchSubmitted?.Invoke(sender, args);
        }

        private void HeaderSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            // Prevent the search box from replacing text with ToString()
            if (args.SelectedItem is SearchSuggestion s && s.IsRichResult)
                sender.Text = sender.Text; // keep current text

            SuggestionChosen?.Invoke(sender, args);
        }

        public void ClearSearch()
        {
            HeaderSearchBox.Text = "";
        }

        private void SuggestionItem_Loaded(object sender, RoutedEventArgs e)
        {
            // Bypass AutoSuggestBox pointer handling
            if (sender is UIElement el)
                el.AddHandler(UIElement.RightTappedEvent,
                    new RightTappedEventHandler(SuggestionItem_RightTapped), true);
        }

        private void SuggestionItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is SearchSuggestion s)
            {
                var menu = new MenuFlyout();

                TrackContextMenu.PopulateForSuggestion(menu, s, App.Queue, (t, p) => NavigationRequested?.Invoke(t, p));

                if (menu.Items.Count > 0)
                {
                    menu.ShowAt(this, new FlyoutShowOptions
                    {
                        Position = e.GetPosition(this),
                        ShowMode = FlyoutShowMode.Transient
                    });
                }

                e.Handled = true;
            }
        }
    }
}
