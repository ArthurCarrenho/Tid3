using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using TidalUi3.Models;
using TidalUi3.Services;
using TidalUi3.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace TidalUi3.Pages;

public sealed partial class ArtistPage : Page
{
    private readonly TidalApiClient _api = App.ApiClient;
    private readonly QueueService _queue = App.Queue;
    private readonly ResourceLoader _resourceLoader = new();
    private List<Track>? _topTracks;
    private int _artistId;
    private bool _bioExpanded;

    public ArtistPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int artistId)
        {
            _artistId = artistId;
            LoadArtist(artistId);
        }
    }

    private async void LoadArtist(int artistId)
    {
        ArtistLoading.IsLoading = true;

        try
        {
            // Fetch everything in parallel
            var artistTask = _api.GetArtistAsync(artistId);
            var topTracksTask = _api.GetArtistTopTracksAsync(artistId, limit: 10);
            var albumsTask = _api.GetArtistAlbumsAsync(artistId, limit: 50);
            var bioTask = _api.GetArtistBioAsync(artistId);
            var singlesTask = _api.GetArtistSinglesAsync(artistId, limit: 20);
            var appearsOnTask = _api.GetArtistAppearsOnAsync(artistId, limit: 20);

            await System.Threading.Tasks.Task.WhenAll(
                artistTask, topTracksTask, albumsTask, bioTask, singlesTask, appearsOnTask);

            var artist = artistTask.Result;
            var topTracks = topTracksTask.Result;
            var albums = albumsTask.Result;
            var bio = bioTask.Result;
            var singles = singlesTask.Result;
            var appearsOn = appearsOnTask.Result;

            // ── Artist header ──
            ArtistName.Text = artist.Name;

            var imageUrl = TidalApiClient.GetImageUrl(artist.Picture, 480, 480);
            if (!string.IsNullOrEmpty(imageUrl))
                ArtistImageControl.ImageSource = imageUrl;

            // Roles (e.g. "Artist · Songwriter · Producer")
            if (artist.ArtistRoles is { Count: > 0 })
            {
                var roles = artist.ArtistRoles
                    .Select(r => r.Category)
                    .Distinct()
                    .ToList();
                ArtistRoles.Text = string.Join(" · ", roles);
                ArtistRoles.Visibility = Visibility.Visible;
            }

            // ── Bio ──
            if (bio is not null && !string.IsNullOrWhiteSpace(bio.Text))
            {
                BioText.Text = ParseBioText(bio.Text);
                BioSection.Visibility = Visibility.Visible;
            }

            // ── Top Tracks ──
            var index = 1;
            _topTracks = topTracks.Items.Select(t => TrackMapper.Map(t, index++)).ToList();
            TopTracksList.ItemsSource = new ObservableCollection<Track>(_topTracks);

            // ── Albums (deduplicated) ──
            if (albums.Items.Count > 0)
            {
                var albumItems = DeduplicateAlbums(albums.Items).Select(a => MapAlbumCard(a)).ToList();
                AlbumsCarousel.ItemsSource = new ObservableCollection<HomeSectionItem>(albumItems);
                AlbumsCarousel.Visibility = Visibility.Visible;
            }

            // ── Singles & EPs (deduplicated) ──
            if (singles.Items.Count > 0)
            {
                var singleItems = DeduplicateAlbums(singles.Items).Select(a => MapAlbumCard(a)).ToList();
                SinglesCarousel.ItemsSource = new ObservableCollection<HomeSectionItem>(singleItems);
                SinglesCarousel.Visibility = Visibility.Visible;
            }

            // ── Appears On (deduplicated) ──
            if (appearsOn.Items.Count > 0)
            {
                var appearsOnItems = DeduplicateAlbums(appearsOn.Items).Select(a => MapAlbumCard(a)).ToList();
                AppearsOnCarousel.ItemsSource = new ObservableCollection<HomeSectionItem>(appearsOnItems);
                AppearsOnCarousel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            ArtistName.Text = _resourceLoader.GetString("Artist_LoadError");
            System.Diagnostics.Debug.WriteLine($"ArtistPage error: {ex}");
        }
        finally
        {
            ArtistLoading.IsLoading = false;
        }
    }

    /// <summary>Deduplicate albums with the same title, keeping the highest-popularity version.</summary>
    private static List<TidalAlbum> DeduplicateAlbums(List<TidalAlbum> albums)
    {
        return albums
            .GroupBy(a => a.Title?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.Popularity).First())
            .ToList();
    }

    private static HomeSectionItem MapAlbumCard(TidalAlbum a)
    {
        var loader = new ResourceLoader();
        // Build subtitle: "2023 · Album" or "2024 · Single · Dolby Atmos"
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(a.ReleaseDate) && a.ReleaseDate.Length >= 4)
            parts.Add(a.ReleaseDate[..4]);

        if (!string.IsNullOrEmpty(a.Type))
        {
            parts.Add(a.Type switch
            {
                "ALBUM" => loader.GetString("Artist_AlbumType_Album"),
                "SINGLE" => loader.GetString("Artist_AlbumType_Single"),
                "EP" => loader.GetString("Artist_AlbumType_EP"),
                _ => a.Type
            });
        }

        // Quality badge
        var tags = a.MediaMetadata?.Tags ?? new List<string>();
        if (tags.Contains("DOLBY_ATMOS"))
            parts.Add(loader.GetString("Artist_Quality_Atmos"));
        else if (tags.Contains("HIRES_LOSSLESS"))
            parts.Add(loader.GetString("Artist_Quality_HiRes"));

        return new HomeSectionItem
        {
            Id = a.Id.ToString(),
            NumericId = a.Id,
            Title = a.Title,
            Subtitle = string.Join(" · ", parts),
            CoverUrl = TidalApiClient.GetImageUrl(a.Cover, 320, 320),
            ItemType = "album",
            IsExplicit = a.Explicit
        };
    }

    private static string ParseBioText(string raw)
    {
        // Strip [wimpLink ...] ... [/wimpLink] markup, keep inner text
        var text = Regex.Replace(raw, @"\[wimpLink[^\]]*\]", "");
        text = text.Replace("[/wimpLink]", "");
        // Convert <br/> to newlines
        text = Regex.Replace(text, @"<br\s*/?>", "\n");
        // Strip any remaining HTML tags
        text = Regex.Replace(text, @"<[^>]+>", "");
        return text.Trim();
    }

    private void BioToggle_Click(object sender, RoutedEventArgs e)
    {
        _bioExpanded = !_bioExpanded;
        BioText.MaxLines = _bioExpanded ? 0 : 4;
        BioToggle.Content = _bioExpanded ? _resourceLoader.GetString("Artist_Bio_ShowLess") : _resourceLoader.GetString("Artist_Bio_ShowMore");
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topTracks is { Count: > 0 })
            _queue.PlayTracks(_topTracks, 0);
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_topTracks is { Count: > 0 })
        {
            _queue.Shuffle = true;
            _queue.PlayTracks(_topTracks, 0);
        }
    }

    private void TrackItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track && _topTracks is not null)
        {
            var idx = _topTracks.IndexOf(track);
            _queue.PlayTracks(_topTracks, idx >= 0 ? idx : 0);
        }
    }

    private void TrackItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        => TrackContextMenu.Show(sender, e, _queue, (t, p) => Frame.Navigate(t, p));

    private void AlbumItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is HomeSectionItem item && item.NumericId > 0)
            Frame.Navigate(typeof(PlaylistDetailPage), item.NumericId);
    }
}
