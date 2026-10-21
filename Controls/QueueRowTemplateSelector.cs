using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Models;

namespace TidalUi3.Controls;

/// <summary>Picks the track row or the matching section title for a queue list item.</summary>
public sealed partial class QueueRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate TrackTemplate { get; set; } = null!;
    public DataTemplate HistoryHeaderTemplate { get; set; } = null!;
    public DataTemplate NowPlayingHeaderTemplate { get; set; } = null!;
    public DataTemplate UpNextHeaderTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item)
    {
        if (ReferenceEquals(item, QueueSection.History)) return HistoryHeaderTemplate;
        if (ReferenceEquals(item, QueueSection.NowPlaying)) return NowPlayingHeaderTemplate;
        if (ReferenceEquals(item, QueueSection.UpNext)) return UpNextHeaderTemplate;
        return TrackTemplate;
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
