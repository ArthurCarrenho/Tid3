using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class CloseButtonControl : UserControl
    {
        public event RoutedEventHandler? Click;

        public static readonly DependencyProperty ToolTipTextProperty =
            DependencyProperty.Register("ToolTipText", typeof(string), typeof(CloseButtonControl),
                new PropertyMetadata("Close", OnToolTipTextChanged));

        public string ToolTipText
        {
            get => (string)GetValue(ToolTipTextProperty);
            set => SetValue(ToolTipTextProperty, value);
        }

        private static void OnToolTipTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CloseButtonControl control && e.NewValue is string tooltip)
                Helpers.Loc.Label(control.InnerButton, tooltip);
        }

        public CloseButtonControl()
        {
            this.InitializeComponent();
            // Until a page sets a more specific one through x:Uid ("Close queue").
            Helpers.Loc.Label(InnerButton, Helpers.Loc.Get("Common_Close"));
        }

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            Click?.Invoke(this, e);
        }
    }
}
