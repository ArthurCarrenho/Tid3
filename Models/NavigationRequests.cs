using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TidalUi3.Services;

namespace TidalUi3.Models;

/// <summary>Opens a TIDAL page (a genre, a mood, "New") in BrowsePage.</summary>
public sealed record BrowseRequest(string ApiPath, string Title);

/// <summary>One page of results; <paramref name="HasMore"/> says whether asking again yields more.</summary>
public sealed record ItemsPage<T>(IReadOnlyList<T> Items, bool HasMore);

/// <summary>
/// A grid of cards that loads page by page as it scrolls, shown by ItemsGridPage: the library's
/// "see all", search's "more", a page module's "show more". The loader gets the number of
/// items loaded so far as its offset.
/// </summary>
public sealed record ItemsGridRequest(string Title, Func<int, CancellationToken, Task<ItemsPage<HomeSectionItem>>> LoadPageAsync);

/// <summary>A list of tracks loaded page by page, shown by PlaylistDetailPage with play and shuffle.</summary>
public sealed record TrackListRequest(
    string Title,
    string Subtitle,
    Func<int, CancellationToken, Task<ItemsPage<TidalTrack>>> LoadPageAsync,
    int MaxTracks = 1000);
