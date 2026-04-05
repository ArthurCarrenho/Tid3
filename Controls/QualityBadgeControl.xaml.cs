using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class QualityBadgeControl : UserControl
    {
        public static readonly DependencyProperty QualityProperty =
            DependencyProperty.Register("Quality", typeof(string), typeof(QualityBadgeControl),
                new PropertyMetadata(string.Empty, OnQualityChanged));

        public string Quality
        {
            get => (string)GetValue(QualityProperty);
            set => SetValue(QualityProperty, value);
        }

        private static void OnQualityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QualityBadgeControl control)
            {
                control.UpdateBadgeText();
            }
        }

        private void UpdateBadgeText()
        {
            var quality = Quality?.ToUpperInvariant() ?? string.Empty;
            BadgeText.Text = quality switch
            {
                "HI_RES_LOSSLESS" => "Hi-Res",
                "LOSSLESS" => "Lossless",
                "DOLBY_ATMOS" => "Atmos",
                "HIGH" => "High",
                "LOW" => "Low",
                _ => quality
            };
        }

        public QualityBadgeControl()
        {
            this.InitializeComponent();
        }
    }
}
