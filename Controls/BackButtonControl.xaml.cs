using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class BackButtonControl : UserControl
    {
        public static readonly DependencyProperty ShowLabelProperty =
            DependencyProperty.Register("ShowLabel", typeof(bool), typeof(BackButtonControl), new PropertyMetadata(false, OnShowLabelChanged));

        public bool ShowLabel
        {
            get => (bool)GetValue(ShowLabelProperty);
            set => SetValue(ShowLabelProperty, value);
        }

        private static void OnShowLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BackButtonControl control)
            {
                bool show = (bool)e.NewValue;
                control.LabelText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    // Switch to text-style button
                    control.InnerButton.Style = null;
                    control.InnerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    control.InnerButton.BorderThickness = new Thickness(0);
                    control.InnerButton.Width = double.NaN;
                    control.InnerButton.Height = double.NaN;
                    control.InnerButton.Padding = new Thickness(8, 4, 8, 4);
                    control.InnerButton.Margin = new Thickness(-8, 0, 0, -8);
                }
            }
        }

        public BackButtonControl()
        {
            this.InitializeComponent();
        }

        private void InnerButton_Click(object sender, RoutedEventArgs e)
        {
            // Walk up to find the Frame and go back
            var current = (DependencyObject)this;
            while (current != null)
            {
                if (current is Page page && page.Frame?.CanGoBack == true)
                {
                    page.Frame.GoBack();
                    return;
                }
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }
        }
    }
}
