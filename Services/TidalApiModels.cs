using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TidalUi3.Services;

// ---------- Device Auth ----------

public class DeviceAuthResponse
{
    [JsonPropertyName("deviceCode")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("userCode")]
    public string UserCode { get; set; } = string.Empty;

    [JsonPropertyName("verificationUri")]
    public string VerificationUri { get; set; } = string.Empty;

    [JsonPropertyName("verificationUriComplete")]
    public string VerificationUriComplete { get; set; } = string.Empty;

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("user")]
    public TidalUser? User { get; set; }
}

public class TidalUser
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("id")]
    public int Id { get => UserId; set => UserId = value; }

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("picture")]
    public string Picture { get; set; } = string.Empty;

    [JsonPropertyName("newsletter")]
    public bool Newsletter { get; set; }

    // Sometimes subscription details are attached or separate, 
    // but the users endpoint usually returns country Code and minimal info.
    // If there's subscription level info, it can go here.
}

// ---------- Paged responses ----------

public class TidalPagedResult<T>
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("totalNumberOfItems")]
    public int TotalNumberOfItems { get; set; }

    [JsonPropertyName("items")]
    public List<T> Items { get; set; } = [];
}

// ---------- Search ----------

public class TidalSearchResult
{
    [JsonPropertyName("artists")]
    public TidalPagedResult<TidalArtist>? Artists { get; set; }

    [JsonPropertyName("albums")]
    public TidalPagedResult<TidalAlbum>? Albums { get; set; }

    [JsonPropertyName("tracks")]
    public TidalPagedResult<TidalTrack>? Tracks { get; set; }

    [JsonPropertyName("playlists")]
    public TidalPagedResult<TidalPlaylist>? Playlists { get; set; }
}

// ---------- Core entities ----------

public class TidalArtistRole
{
    [JsonPropertyName("categoryId")]
    public int CategoryId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
}

public class TidalArtistBio
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
}

public class TidalArtist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("artistRoles")]
    public List<TidalArtistRole> ArtistRoles { get; set; } = new();

    [JsonPropertyName("mixes")]
    public Dictionary<string, string>? Mixes { get; set; }
}

public class TidalAlbum
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("numberOfTracks")]
    public int NumberOfTracks { get; set; }

    [JsonPropertyName("numberOfVolumes")]
    public int NumberOfVolumes { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }

    [JsonPropertyName("audioQuality")]
    public string AudioQuality { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public TidalArtist? Artist { get; set; }

    [JsonPropertyName("artists")]
    public List<TidalArtist> Artists { get; set; } = [];

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("explicit")]
    public bool Explicit { get; set; }

    [JsonPropertyName("mediaMetadata")]
    public TidalMediaMetadata? MediaMetadata { get; set; }
}

public class TidalTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("trackNumber")]
    public int TrackNumber { get; set; }

    [JsonPropertyName("volumeNumber")]
    public int VolumeNumber { get; set; }

    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }

    [JsonPropertyName("audioQuality")]
    public string AudioQuality { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public TidalArtist? Artist { get; set; }

    [JsonPropertyName("artists")]
    public List<TidalArtist> Artists { get; set; } = [];

    [JsonPropertyName("album")]
    public TidalAlbum? Album { get; set; }

    [JsonPropertyName("allowStreaming")]
    public bool AllowStreaming { get; set; }

    [JsonPropertyName("streamReady")]
    public bool StreamReady { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("explicit")]
    public bool Explicit { get; set; }

    [JsonPropertyName("mediaMetadata")]
    public TidalMediaMetadata? MediaMetadata { get; set; }
}

public class TidalMediaMetadata
{
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}

public class TidalTopHitsResult
{
    [JsonPropertyName("artists")]
    public TidalPagedResult<TidalArtist>? Artists { get; set; }

    [JsonPropertyName("albums")]
    public TidalPagedResult<TidalAlbum>? Albums { get; set; }

    [JsonPropertyName("tracks")]
    public TidalPagedResult<TidalTrack>? Tracks { get; set; }
}

public class TidalTrackItem
{
    [JsonPropertyName("item")]
    public TidalTrack? Item { get; set; }

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;
}

public class TidalAlbumItem
{
    [JsonPropertyName("item")]
    public TidalAlbum? Item { get; set; }

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;
}

public class TidalPlaylistItem
{
    [JsonPropertyName("item")]
    public TidalPlaylist? Item { get; set; }

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;
}

public class TidalPlaylist
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("numberOfTracks")]
    public int NumberOfTracks { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("squareImage")]
    public string? SquareImage { get; set; }

    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("creator")]
    public TidalPlaylistCreator? Creator { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public class TidalPlaylistCreator
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

// ---------- Streaming ----------

public class TidalStreamUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("trackId")]
    public int TrackId { get; set; }

    [JsonPropertyName("audioQuality")]
    public string AudioQuality { get; set; } = string.Empty;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;
}

public class TidalPlaybackInfo
{
    [JsonPropertyName("trackId")]
    public int TrackId { get; set; }

    [JsonPropertyName("assetPresentation")]
    public string AssetPresentation { get; set; } = string.Empty;

    [JsonPropertyName("audioQuality")]
    public string AudioQuality { get; set; } = string.Empty;

    [JsonPropertyName("audioMode")]
    public string AudioMode { get; set; } = string.Empty;

    [JsonPropertyName("manifest")]
    public string Manifest { get; set; } = string.Empty;

    [JsonPropertyName("manifestMimeType")]
    public string ManifestMimeType { get; set; } = string.Empty;
}

public class TidalManifest
{
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("codecs")]
    public string Codecs { get; set; } = string.Empty;

    [JsonPropertyName("urls")]
    public List<string> Urls { get; set; } = [];
}

// ---------- Lyrics ----------

public class TritonLyricsResponse
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("lyrics")]
    public TritonLyricsData? Lyrics { get; set; }
}

public class TritonLyricsData
{
    [JsonPropertyName("trackId")]
    public int TrackId { get; set; }

    [JsonPropertyName("lyrics")]
    public string Lyrics { get; set; } = string.Empty;

    [JsonPropertyName("subtitles")]
    public string Subtitles { get; set; } = string.Empty;
}

public class TidalLyricsSubtitle
{
    [JsonPropertyName("startTimeMs")]
    public int StartTimeMs { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
