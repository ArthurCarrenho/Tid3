using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class IconButtonControl : UserControl
    {
        public event RoutedEventHandler? Click;

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register("Glyph", typeof(string), typeof(IconButtonControl), new PropertyMetadata(string.Empty, OnGlyphChanged));

        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IconButtonControl control)
                control.ButtonIcon.Glyph = (string)e.NewValue;
        }

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(IconButtonControl), new PropertyMetadata(string.Empty, OnTextChanged));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IconButtonControl control)
                control.ButtonText.Text = (string)e.NewValue;
        }

        public static readonly DependencyProperty IsAccentProperty =
            DependencyProperty.Register("IsAccent", typeof(bool), typeof(IconButtonControl), new PropertyMetadata(false, OnIsAccentChanged));

        public bool IsAccent
        {
            get => (bool)GetValue(IsAccentProperty);
            set => SetValue(IsAccentProperty, value);
        }

        private static void OnIsAccentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IconButtonControl control)
            {
                control.InnerButton.Style = (bool)e.NewValue
                    ? (Style)Application.Current.Resources["AccentButtonStyle"]
                    : (Style)Application.Current.Resources["SubtleButtonStyle"];
            }
        }

        public IconButtonControl()
        {
            this.InitializeComponent();
            InnerButton.Style = (Style)Application.Current.Resources["SubtleButtonStyle"];
            InnerButton.Click += (s, e) => Click?.Invoke(this, e);
        }
    }
}
