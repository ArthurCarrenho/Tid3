using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TidalUi3.Helpers;

namespace TidalUi3.Controls
{
    public enum RepeatMode
    {
        None,
        All,
        One
    }

    public sealed partial class TransportControls : UserControl
    {
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register("IsPlaying", typeof(bool), typeof(TransportControls),
                new PropertyMetadata(false, OnIsPlayingChanged));

        public static readonly DependencyProperty IsShuffledProperty =
            DependencyProperty.Register("IsShuffled", typeof(bool), typeof(TransportControls),
                new PropertyMetadata(false, OnIsShuffledChanged));

        public static readonly DependencyProperty RepeatModeProperty =
            DependencyProperty.Register("RepeatMode", typeof(RepeatMode), typeof(TransportControls),
                new PropertyMetadata(RepeatMode.None, OnRepeatModeChanged));

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        public bool IsShuffled
        {
            get => (bool)GetValue(IsShuffledProperty);
            set => SetValue(IsShuffledProperty, value);
        }

        public RepeatMode RepeatMode
        {
            get => (RepeatMode)GetValue(RepeatModeProperty);
            set => SetValue(RepeatModeProperty, value);
        }

        public event RoutedEventHandler? PlayPauseClick;
        public event RoutedEventHandler? PreviousClick;
        public event RoutedEventHandler? NextClick;
        public event RoutedEventHandler? ShuffleClick;
        public event RoutedEventHandler? RepeatClick;

        private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TransportControls control)
            {
                control.PlayPauseIcon.Glyph = control.IsPlaying ? Glyphs.Pause : Glyphs.Play;
            }
        }

        private static void OnIsShuffledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TransportControls control)
            {
                control.ShuffleIcon.Foreground = control.IsShuffled
                    ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            }
        }

        private static void OnRepeatModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TransportControls control)
            {
                var accentBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                var defaultBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

                switch (control.RepeatMode)
                {
                    case RepeatMode.None:
                        control.RepeatIcon.Glyph = Glyphs.Repeat;
                        control.RepeatIcon.Foreground = defaultBrush;
                        break;
                    case RepeatMode.All:
                        control.RepeatIcon.Glyph = Glyphs.Repeat;
                        control.RepeatIcon.Foreground = accentBrush;
                        break;
                    case RepeatMode.One:
                        control.RepeatIcon.Glyph = Glyphs.RepeatOne;
                        control.RepeatIcon.Foreground = accentBrush;
                        break;
                }
            }
        }

        public TransportControls()
        {
            this.InitializeComponent();
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e) => ShuffleClick?.Invoke(this, e);
        private void PreviousButton_Click(object sender, RoutedEventArgs e) => PreviousClick?.Invoke(this, e);
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e) => PlayPauseClick?.Invoke(this, e);
        private void NextButton_Click(object sender, RoutedEventArgs e) => NextClick?.Invoke(this, e);
        private void RepeatButton_Click(object sender, RoutedEventArgs e) => RepeatClick?.Invoke(this, e);
    }
}
