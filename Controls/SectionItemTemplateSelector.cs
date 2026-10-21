using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Models;

namespace TidalUi3.Controls;

/// <summary>Round artist cards, square cards for albums, playlists and mixes.</summary>
public sealed partial class SectionItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate CardTemplate { get; set; } = null!;
    public DataTemplate ArtistTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item) =>
        item is HomeSectionItem { ItemType: "artist" } ? ArtistTemplate : CardTemplate;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
