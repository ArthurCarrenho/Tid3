using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TidalUi3.Helpers;

namespace TidalUi3.Controls
{
    public sealed partial class VolumeControl : UserControl
    {
        public static readonly DependencyProperty VolumeProperty =
            DependencyProperty.Register("Volume", typeof(double), typeof(VolumeControl),
                new PropertyMetadata(75.0, OnVolumeChanged));

        public static readonly DependencyProperty IsMutedProperty =
            DependencyProperty.Register("IsMuted", typeof(bool), typeof(VolumeControl),
                new PropertyMetadata(false, OnIsMutedChanged));

        public double Volume
        {
            get => (double)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, value);
        }

        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        public event RoutedEventHandler? MuteClick;
        public event RoutedEventHandler? VolumeChanged;

        private static void OnVolumeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VolumeControl control && e.NewValue is double volume)
            {
                if (control.VolumeSlider.Value != volume)
                {
                    control.VolumeSlider.Value = volume;
                }
                control.UpdateVolumeIcon();
            }
        }

        private static void OnIsMutedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VolumeControl control)
            {
                control.UpdateVolumeIcon();
            }
        }

        private void UpdateVolumeIcon()
        {
            if (IsMuted || Volume == 0)
            {
                VolumeIcon.Glyph = Glyphs.VolumeMute;
            }
            else if (Volume < 30)
            {
                VolumeIcon.Glyph = Glyphs.Volume0;
            }
            else if (Volume < 70)
            {
                VolumeIcon.Glyph = Glyphs.Volume1;
            }
            else
            {
                VolumeIcon.Glyph = Glyphs.Volume;
            }
        }

        public VolumeControl()
        {
            this.InitializeComponent();
            VolumeSlider.Value = Volume;
            UpdateVolumeIcon();
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            MuteClick?.Invoke(this, e);
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs? e)
        {
            Volume = e.NewValue;
            UpdateVolumeIcon();
            VolumeChanged?.Invoke(this, e);
        }
    }
}
