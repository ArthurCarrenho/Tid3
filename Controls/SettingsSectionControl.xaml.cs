using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls
{
    public sealed partial class SettingsSectionControl : UserControl
    {
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register("Header", typeof(string), typeof(SettingsSectionControl),
                new PropertyMetadata(string.Empty, OnHeaderChanged));

        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register("SectionContent", typeof(UIElement), typeof(SettingsSectionControl),
                new PropertyMetadata(null, OnContentChanged));

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public UIElement SectionContent
        {
            get => (UIElement)GetValue(SectionContentProperty);
            set => SetValue(SectionContentProperty, value);
        }

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsSectionControl control && e.NewValue is string header)
            {
                control.HeaderText.Text = header;
            }
        }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SettingsSectionControl control && e.NewValue is UIElement content)
            {
                control.SectionContentPresenter.Content = content;
            }
        }

        public SettingsSectionControl()
        {
            this.InitializeComponent();
        }
    }
}
