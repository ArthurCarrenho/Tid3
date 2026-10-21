using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class ArtistCardControl : UserControl
    {
        public static readonly DependencyProperty ArtistNameProperty =
            DependencyProperty.Register("ArtistName", typeof(string), typeof(ArtistCardControl), new PropertyMetadata(string.Empty));

        public string ArtistName
        {
            get => (string)GetValue(ArtistNameProperty);
            set => SetValue(ArtistNameProperty, value);
        }

        public static readonly DependencyProperty ArtistImageUrlProperty =
            DependencyProperty.Register("ArtistImageUrl", typeof(string), typeof(ArtistCardControl), new PropertyMetadata(null));

        public string ArtistImageUrl
        {
            get => (string)GetValue(ArtistImageUrlProperty);
            set => SetValue(ArtistImageUrlProperty, value);
        }

        public ArtistCardControl()
        {
            this.InitializeComponent();
            // Same menu as the other cards: open the artist, follow or unfollow.
            RightTapped += (sender, e) =>
            {
                if (FindFrame(this) is { } frame)
                    Helpers.MediaCardContextMenu.Show(sender, e, frame);
            };
        }

        private static Frame? FindFrame(DependencyObject element)
        {
            for (var current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
                if (current is Page page)
                    return page.Frame;
            return null;
        }
    }
}
