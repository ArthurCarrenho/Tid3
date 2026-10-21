using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Helpers;

namespace TidalUi3.Controls
{
    public sealed partial class ImagePlaceholderControl : UserControl
    {
        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(string), typeof(ImagePlaceholderControl), new PropertyMetadata(null, OnImageSourceChanged));

        public string ImageSource
        {
            get => (string)GetValue(ImageSourceProperty);
            set => SetValue(ImageSourceProperty, value);
        }

        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ImagePlaceholderControl control) return;

            if (e.NewValue is not string url || string.IsNullOrEmpty(url))
            {
                control.ContentImage.Source = null;
                return;
            }

            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            // Decode at the size shown when it is fixed. A 48 px row thumbnail otherwise keeps
            // a full 160 px (or 320 px) bitmap in memory, for every visible row and card.
            if (control.Width is > 0 and < double.PositiveInfinity)
            {
                bitmap.DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical;
                bitmap.DecodePixelWidth = (int)System.Math.Ceiling(control.Width);
            }
            bitmap.UriSource = new System.Uri(url);
            control.ContentImage.Source = bitmap;
        }

        public static readonly DependencyProperty FallbackGlyphProperty =
            DependencyProperty.Register("FallbackGlyph", typeof(string), typeof(ImagePlaceholderControl), new PropertyMetadata(Glyphs.Music, OnFallbackGlyphChanged));

        public string FallbackGlyph
        {
            get => (string)GetValue(FallbackGlyphProperty);
            set => SetValue(FallbackGlyphProperty, value);
        }

        private static void OnFallbackGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImagePlaceholderControl control)
                control.FallbackIcon.Glyph = (string)e.NewValue;
        }

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register("IconSize", typeof(double), typeof(ImagePlaceholderControl), new PropertyMetadata(18.0, OnIconSizeChanged));

        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        private static void OnIconSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImagePlaceholderControl control)
                control.FallbackIcon.FontSize = (double)e.NewValue;
        }

        public static readonly DependencyProperty ImageCornerRadiusProperty =
            DependencyProperty.Register("ImageCornerRadius", typeof(CornerRadius), typeof(ImagePlaceholderControl), new PropertyMetadata(new CornerRadius(0), OnCornerRadiusChanged));

        public CornerRadius ImageCornerRadius
        {
            get => (CornerRadius)GetValue(ImageCornerRadiusProperty);
            set => SetValue(ImageCornerRadiusProperty, value);
        }

        private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImagePlaceholderControl control)
                control.ContainerGrid.CornerRadius = (CornerRadius)e.NewValue;
        }

        public ImagePlaceholderControl()
        {
            this.InitializeComponent();
        }
    }
}
