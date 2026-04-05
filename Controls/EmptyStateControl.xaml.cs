using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class EmptyStateControl : UserControl
    {
        public static readonly DependencyProperty IconGlyphProperty =
            DependencyProperty.Register("IconGlyph", typeof(string), typeof(EmptyStateControl),
                new PropertyMetadata(string.Empty, OnIconGlyphChanged));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(EmptyStateControl),
                new PropertyMetadata(string.Empty, OnTitleChanged));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register("Description", typeof(string), typeof(EmptyStateControl),
                new PropertyMetadata(string.Empty, OnDescriptionChanged));

        public string IconGlyph
        {
            get => (string)GetValue(IconGlyphProperty);
            set => SetValue(IconGlyphProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        private static void OnIconGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmptyStateControl control && e.NewValue is string glyph)
            {
                control.StateIcon.Glyph = glyph;
            }
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmptyStateControl control && e.NewValue is string title)
            {
                control.TitleText.Text = title;
            }
        }

        private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmptyStateControl control && e.NewValue is string desc)
            {
                control.DescriptionText.Text = desc;
                control.DescriptionText.Visibility = string.IsNullOrEmpty(desc)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        public EmptyStateControl()
        {
            this.InitializeComponent();
        }
    }
}
