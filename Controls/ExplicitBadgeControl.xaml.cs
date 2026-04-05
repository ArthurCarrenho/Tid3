using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class ExplicitBadgeControl : UserControl
    {
        public static readonly DependencyProperty IsExplicitProperty =
            DependencyProperty.Register("IsExplicit", typeof(bool), typeof(ExplicitBadgeControl), new PropertyMetadata(false, OnIsExplicitChanged));

        public bool IsExplicit
        {
            get => (bool)GetValue(IsExplicitProperty);
            set => SetValue(IsExplicitProperty, value);
        }

        private static void OnIsExplicitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ExplicitBadgeControl control)
                control.BadgeBorder.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public ExplicitBadgeControl()
        {
            this.InitializeComponent();
        }
    }
}
