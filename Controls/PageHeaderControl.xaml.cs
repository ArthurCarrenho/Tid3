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

        // Suggestions may only open the list while the user is in the box. Results that
        // arrive after focus moved on must not pop it back open over the page.
        private bool _isSearchFocused;

        public PageHeaderControl()
        {
            this.InitializeComponent();
            Unloaded += (_, _) => CancelPendingSearch();

            HeaderSearchBox.GotFocus += (_, _) => _isSearchFocused = true;
            HeaderSearchBox.LostFocus += (_, _) =>
            {
                _isSearchFocused = false;
                CancelPendingSearch();
            };
        }

        private void BackButton_Click(object sender, RoutedEventArgs e) => BackClick?.Invoke(sender, e);
        private void ForwardButton_Click(object sender, RoutedEventArgs e) => ForwardClick?.Invoke(sender, e);

        private void HeaderSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
                return;

            CancelPendingSearch();
            var query = sender.Text?.Trim() ?? "";

            if (query.Length < 2 || ApiClient?.IsAuthenticated != true)
            {
                sender.ItemsSource = null;
                sender.IsSuggestionListOpen = false;
                return;
            }

            _searchCts = new CancellationTokenSource();
            _searchDebounceTask = DebounceSearchAsync(sender, query, _searchCts.Token);
        }

        // Dispose the superseded source as well: a new one is created on every
        // keystroke, so cancelling without disposing leaks one per character typed.
        private void CancelPendingSearch()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
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
                    {
                        var artistName = t.Artist?.Name ?? t.Artists.FirstOrDefault()?.Name ?? "";
                        suggestions.Add(new SearchSuggestion
                        {
                            Type = "track",
                            IsRichResult = true,
                            Display = t.Title,
                            Subtitle = Loc.Format("Search_SuggestionTrack", artistName),
                            ArtistName = artistName,
                            CoverUrl = TidalApiClient.GetImageUrl(t.Album?.Cover, 80, 80) ?? "",
                            IsExplicit = t.Explicit,
                            TrackId = t.Id,
                            Name = t.Title,
                            Id = t.Album?.Id ?? 0,
                            ArtistId = t.Artist?.Id ?? t.Artists.FirstOrDefault()?.Id ?? 0
                        });
                    }
                }

                // Rich: Albums
                if (hits.Albums?.Items is { Count: > 0 } albums)
                {
                    foreach (var a in albums.Take(2))
                    {
                        var artistName = a.Artist?.Name ?? a.Artists.FirstOrDefault()?.Name ?? "";
                        suggestions.Add(new SearchSuggestion
                        {
                            Type = "album",
                            IsRichResult = true,
                            Display = a.Title,
                            Subtitle = Loc.Format("Search_SuggestionAlbum", artistName),
                            ArtistName = artistName,
                            CoverUrl = TidalApiClient.GetImageUrl(a.Cover, 80, 80) ?? "",
                            Id = a.Id,
                            Name = a.Title
                        });
                    }
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
                            Subtitle = Loc.Get("Search_SuggestionArtist"),
                            CoverUrl = TidalApiClient.GetImageUrl(a.Picture, 80, 80) ?? "",
                            Id = a.Id,
                            Name = a.Name
                        });
                }

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    // Stale if the box was cleared (search submitted), edited, or left since.
                    // Assigning ItemsSource reopens the list, which is what kept it stuck open.
                    if (ct.IsCancellationRequested || !_isSearchFocused
                        || (box.Text?.Trim() ?? "") != query)
                        return;
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

        // UpdateTextOnSelect="False" in XAML keeps the typed text when suggestions are
        // browsed with the arrow keys, so nothing needs to be restored here.
        private void HeaderSearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
            => SuggestionChosen?.Invoke(sender, args);

        /// <summary>
        /// Empties the box and closes the suggestion list. A search still in flight is
        /// cancelled too; otherwise its results land afterwards and reopen the list.
        /// </summary>
        public void ClearSearch()
        {
            CloseSuggestions();
            HeaderSearchBox.ItemsSource = null;
            HeaderSearchBox.Text = "";
        }

        /// <summary>Puts the cursor in the search box (Ctrl+F).</summary>
        public void FocusSearch()
        {
            HeaderSearchBox.Focus(FocusState.Keyboard);
        }

        /// <summary>
        /// Closes the suggestion list but keeps the typed text. The list only closes by
        /// itself when the box loses focus, which clicking a non-focusable part of the page
        /// does not do.
        /// </summary>
        public void CloseSuggestions()
        {
            CancelPendingSearch();
            HeaderSearchBox.IsSuggestionListOpen = false;
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

                TrackContextMenu.PopulateForSuggestion(menu, s, App.Queue, (t, p) =>
                {
                    NavigationRequested?.Invoke(t, p);
                    ClearSearch();
                });

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
