using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TidalUi3.Models;

public class Track : INotifyPropertyChanged
{
    private bool _isPlaying;
    private bool _isPlayed;
    private bool _isFirstUpcoming;

    public int Id { get; set; }
    public int RowNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string Duration { get; set; } = "0:00";
    public double DurationSeconds { get; set; }
    public string AlbumArtGlyph { get; set; } = "\uE8D6";
    public string CoverUrl { get; set; } = string.Empty;
    public string AudioQuality { get; set; } = string.Empty;
    public int AlbumId { get; set; }
    public int ArtistId { get; set; }
    public string QualityBadge { get; set; } = string.Empty;
    public bool IsLiked { get; set; }
    public bool IsExplicit { get; set; }

    public bool IsUpcoming => !_isPlaying && !_isPlayed;

    public bool IsFirstUpcoming
    {
        get => _isFirstUpcoming;
        set
        {
            if (_isFirstUpcoming != value)
            {
                _isFirstUpcoming = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlayed
    {
        get => _isPlayed;
        set
        {
            if (_isPlayed != value)
            {
                _isPlayed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUpcoming));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public Track Clone()
    {
        return new Track
        {
            Id = this.Id,
            RowNumber = this.RowNumber,
            Title = this.Title,
            Artist = this.Artist,
            Album = this.Album,
            Duration = this.Duration,
            DurationSeconds = this.DurationSeconds,
            AlbumArtGlyph = this.AlbumArtGlyph,
            CoverUrl = this.CoverUrl,
            AudioQuality = this.AudioQuality,
            AlbumId = this.AlbumId,
            ArtistId = this.ArtistId,
            QualityBadge = this.QualityBadge,
            IsLiked = this.IsLiked,
            IsExplicit = this.IsExplicit
        };
    }
}
