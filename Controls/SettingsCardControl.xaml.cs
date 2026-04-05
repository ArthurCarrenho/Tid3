using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TidalUi3.Controls;

public sealed partial class SettingsCardControl : UserControl
{
    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register("IconGlyph", typeof(string), typeof(SettingsCardControl),
            new PropertyMetadata("\uE713", OnIconGlyphChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register("Title", typeof(string), typeof(SettingsCardControl),
            new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register("Description", typeof(string), typeof(SettingsCardControl),
            new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register("ActionContent", typeof(UIElement), typeof(SettingsCardControl),
            new PropertyMetadata(null, OnActionContentChanged));

    public static readonly DependencyProperty IsDevPreviewProperty =
        DependencyProperty.Register("IsDevPreview", typeof(bool), typeof(SettingsCardControl),
            new PropertyMetadata(false, OnIsDevPreviewChanged));

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

    public UIElement ActionContent
    {
        get => (UIElement)GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public bool IsDevPreview
    {
        get => (bool)GetValue(IsDevPreviewProperty);
        set => SetValue(IsDevPreviewProperty, value);
    }

    private static void OnIconGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCardControl c && e.NewValue is string glyph)
            c.CardIcon.Glyph = glyph;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCardControl c && e.NewValue is string title)
            c.TitleText.Text = title;
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCardControl c && e.NewValue is string desc)
        {
            c.DescriptionText.Text = desc;
            c.DescriptionText.Visibility = string.IsNullOrEmpty(desc) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void OnActionContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCardControl c && e.NewValue is UIElement content)
            c.ActionPresenter.Content = content;
    }

    private static void OnIsDevPreviewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCardControl c && e.NewValue is bool isDev)
        {
            c.DevBadge.Visibility = isDev ? Visibility.Visible : Visibility.Collapsed;
            c.RootCard.Opacity = isDev ? 0.55 : 1.0;
        }
    }

    public SettingsCardControl()
    {
        InitializeComponent();
    }
}
