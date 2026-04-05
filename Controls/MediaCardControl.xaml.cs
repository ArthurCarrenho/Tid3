using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TidalUi3.Helpers;

namespace TidalUi3.Controls
{
    public sealed partial class MediaCardControl : UserControl
    {
        public static readonly DependencyProperty CardWidthProperty =
            DependencyProperty.Register("CardWidth", typeof(double), typeof(MediaCardControl), new PropertyMetadata(180.0));

        public double CardWidth
        {
            get => (double)GetValue(CardWidthProperty);
            set => SetValue(CardWidthProperty, value);
        }

        public static readonly DependencyProperty FallbackGlyphProperty =
            DependencyProperty.Register("FallbackGlyph", typeof(string), typeof(MediaCardControl), new PropertyMetadata("\xE93C"));

        public string FallbackGlyph
        {
            get => (string)GetValue(FallbackGlyphProperty);
            set => SetValue(FallbackGlyphProperty, value);
        }

        public static readonly DependencyProperty ImageUrlProperty =
            DependencyProperty.Register("ImageUrl", typeof(string), typeof(MediaCardControl), new PropertyMetadata(null));

        public string ImageUrl
        {
            get => (string)GetValue(ImageUrlProperty);
            set => SetValue(ImageUrlProperty, value);
        }

        public static readonly DependencyProperty TitleTextProperty =
            DependencyProperty.Register("TitleText", typeof(string), typeof(MediaCardControl), new PropertyMetadata(string.Empty));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }

        public static readonly DependencyProperty SubtitleTextProperty =
            DependencyProperty.Register("SubtitleText", typeof(string), typeof(MediaCardControl), new PropertyMetadata(string.Empty));

        public string SubtitleText
        {
            get => (string)GetValue(SubtitleTextProperty);
            set => SetValue(SubtitleTextProperty, value);
        }

        public static readonly DependencyProperty IsExplicitProperty =
            DependencyProperty.Register("IsExplicit", typeof(bool), typeof(MediaCardControl), new PropertyMetadata(false));

        public bool IsExplicit
        {
            get => (bool)GetValue(IsExplicitProperty);
            set => SetValue(IsExplicitProperty, value);
        }

        public MediaCardControl()
        {
            this.InitializeComponent();
            this.RightTapped += MediaCardControl_RightTapped;
        }

        private void MediaCardControl_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var frame = FindFrame(this);
            if (frame != null)
                MediaCardContextMenu.Show(sender, e, frame);
        }

        private static Frame? FindFrame(DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                if (current is Page page)
                    return page.Frame;
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
