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
        }
    }
}
