using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Models;
using TidalUi3.Pages;

namespace TidalUi3.Helpers;

/// <summary>A track's credits (composers, producers, musicians) in a dialog; names with a TIDAL page link to it.</summary>
public static class CreditsDialog
{
    public static async Task ShowAsync(Track track, Action<Type, object> navigate)
    {
        if (App.MainWindow?.Content is not FrameworkElement root || track.Id <= 0) return;

        var content = new StackPanel { Spacing = 16, MinWidth = 360 };
        content.Children.Add(new ProgressRing { IsActive = true, HorizontalAlignment = HorizontalAlignment.Center });

        var dialog = new ContentDialog
        {
            Title = track.Title,
            Content = new ScrollViewer { Content = content, MaxHeight = 480 },
            CloseButtonText = Loc.Get("Common_Close"),
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.RequestedTheme,
        };

        // The dialog opens at once with a spinner; the credits fill it in when they arrive.
        Task<ContentDialogResult> shown;
        try
        {
            shown = dialog.ShowAsync().AsTask();
        }
        catch (Exception ex)
        {
            AppLog.Exception("CreditsDialogError", ex);
            return;
        }

        try
        {
            var credits = await App.ApiClient.GetTrackCreditsAsync(track.Id);
            content.Children.Clear();
            if (credits.Count == 0)
                content.Children.Add(Text(Loc.Get("Credits_None"), secondary: true));

            foreach (var role in credits)
            {
                var group = new StackPanel { Spacing = 2 };
                group.Children.Add(new TextBlock
                {
                    Text = role.Type,
                    Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                });
                foreach (var contributor in role.Contributors)
                {
                    if (contributor.Id is int artistId && artistId > 0)
                    {
                        var link = new HyperlinkButton { Content = contributor.Name, Padding = new Thickness(0) };
                        link.Click += (_, _) =>
                        {
                            dialog.Hide();
                            navigate(typeof(ArtistPage), artistId);
                        };
                        group.Children.Add(link);
                    }
                    else
                    {
                        group.Children.Add(Text(contributor.Name));
                    }
                }
                content.Children.Add(group);
            }
        }
        catch (Exception ex)
        {
            AppLog.Exception("CreditsLoadError", ex);
            content.Children.Clear();
            content.Children.Add(Text(Loc.Get("Credits_LoadFailed"), secondary: true));
        }

        await shown;
    }

    private static TextBlock Text(string text, bool secondary = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
        Foreground = secondary
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
    };
}
