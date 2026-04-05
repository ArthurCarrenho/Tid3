using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class LoadingOverlayControl : UserControl
    {
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register("IsLoading", typeof(bool), typeof(LoadingOverlayControl), new PropertyMetadata(false, OnIsLoadingChanged));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LoadingOverlayControl control)
            {
                bool loading = (bool)e.NewValue;
                control.Ring.IsActive = loading;
                control.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public LoadingOverlayControl()
        {
            this.InitializeComponent();
        }
    }
}
