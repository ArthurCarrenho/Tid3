using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TidalUi3.Controls;

namespace TidalUi3.Helpers;

/// <summary>
/// Encapsulates the standard page loading pattern with loading overlay and error handling.
/// </summary>
public sealed class PageLoader
{
    private readonly LoadingOverlayControl _loadingOverlay;
    private readonly Action<string>? _onError;

    public PageLoader(LoadingOverlayControl loadingOverlay, Action<string>? onError = null)
    {
        _loadingOverlay = loadingOverlay;
        _onError = onError;
    }

    /// <summary>
    /// Executes a loading operation with proper loading state management and error handling.
    /// </summary>
    /// <param name="loadAction">The async action to execute</param>
    /// <param name="errorMessage">The error message to display on failure</param>
    public async Task LoadAsync(Func<Task> loadAction, string errorMessage)
    {
        _loadingOverlay.IsLoading = true;

        try
        {
            await loadAction();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{errorMessage}: {ex}");
            _onError?.Invoke(errorMessage);
        }
        finally
        {
            _loadingOverlay.IsLoading = false;
        }
    }

    /// <summary>
    /// Executes a loading operation that returns a result.
    /// </summary>
    /// <typeparam name="T">The return type</typeparam>
    /// <param name="loadFunc">The async function to execute</param>
    /// <param name="errorMessage">The error message to display on failure</param>
    /// <param name="defaultValue">The default value to return on failure</param>
    /// <returns>The result of the operation, or defaultValue on failure</returns>
    public async Task<T?> LoadAsync<T>(Func<Task<T>> loadFunc, string errorMessage, T? defaultValue = default)
    {
        _loadingOverlay.IsLoading = true;

        try
        {
            return await loadFunc();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{errorMessage}: {ex}");
            _onError?.Invoke(errorMessage);
            return defaultValue;
        }
        finally
        {
            _loadingOverlay.IsLoading = false;
        }
    }
}

/// <summary>
/// Extensions for UI dispatcher operations.
/// </summary>
public static class DispatcherExtensions
{
    /// <summary>
    /// Safely updates a UI property on the dispatcher thread.
    /// </summary>
    public static void SetText(this TextBlock textBlock, string text)
    {
        textBlock.Text = text;
    }

    /// <summary>
    /// Safely updates an Image source on the dispatcher thread.
    /// </summary>
    public static void SetSource(this ImagePlaceholderControl imageControl, string? url)
    {
        if (!string.IsNullOrEmpty(url))
            imageControl.ImageSource = url;
    }
}
