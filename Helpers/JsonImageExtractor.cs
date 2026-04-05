using System.Text.Json;
using TidalUi3.Services;

namespace TidalUi3.Helpers;

/// <summary>
/// Extracts image URLs from JSON elements with various Tidal API image property patterns.
/// </summary>
public static class JsonImageExtractor
{
    /// <summary>
    /// Tries to extract an image URL from a JsonElement using multiple fallback strategies.
    /// Checks for: images.{size}.url, squareImage, image, cover properties.
    /// </summary>
    /// <param name="element">The JSON element to extract from</param>
    /// <param name="width">Desired width for Tidal image URLs</param>
    /// <param name="height">Desired height for Tidal image URLs</param>
    /// <param name="preferredSize">Preferred image size key (e.g., "MEDIUM", "LARGE")</param>
    /// <returns>The image URL if found, otherwise null</returns>
    public static string? ExtractImageUrl(JsonElement element, int width = 320, int height = 320, string preferredSize = "MEDIUM")
    {
        // Try images.{preferredSize}.url first (common in mixes/playlists)
        if (TryGetImagesUrl(element, preferredSize, out var url))
            return url;

        // Try images.LARGE as fallback
        if (preferredSize != "LARGE" && TryGetImagesUrl(element, "LARGE", out url))
            return url;

        // Try images.MEDIUM as fallback
        if (preferredSize != "MEDIUM" && TryGetImagesUrl(element, "MEDIUM", out url))
            return url;

        // Try squareImage (common in mixes)
        if (element.TryGetProperty("squareImage", out var squareImage) &&
            squareImage.ValueKind == JsonValueKind.String)
        {
            return TidalApiClient.GetImageUrl(squareImage.GetString(), width, height);
        }

        // Try image property
        if (element.TryGetProperty("image", out var image) &&
            image.ValueKind == JsonValueKind.String)
        {
            return TidalApiClient.GetImageUrl(image.GetString(), width, height);
        }

        // Try cover property (common in albums)
        if (element.TryGetProperty("cover", out var cover) &&
            cover.ValueKind == JsonValueKind.String)
        {
            return TidalApiClient.GetImageUrl(cover.GetString(), width, height);
        }

        return null;
    }

    /// <summary>
    /// Tries to extract an image URL from the images.{size}.url pattern.
    /// </summary>
    private static bool TryGetImagesUrl(JsonElement element, string size, out string? url)
    {
        url = null;

        if (element.TryGetProperty("images", out var images) &&
            images.ValueKind == JsonValueKind.Object &&
            images.TryGetProperty(size, out var sizeObj) &&
            sizeObj.ValueKind == JsonValueKind.Object &&
            sizeObj.TryGetProperty("url", out var urlElement) &&
            urlElement.ValueKind == JsonValueKind.String)
        {
            url = urlElement.GetString();
            return !string.IsNullOrEmpty(url);
        }

        return false;
    }

    /// <summary>
    /// Tries to extract an image ID from various property patterns.
    /// </summary>
    /// <param name="element">The JSON element to extract from</param>
    /// <returns>The image ID if found, otherwise null</returns>
    public static string? ExtractImageId(JsonElement element)
    {
        // Try images.MEDIUM.url first
        if (TryGetImagesUrl(element, "MEDIUM", out var url) && !string.IsNullOrEmpty(url))
            return ExtractIdFromUrl(url);

        // Try images.LARGE.url
        if (TryGetImagesUrl(element, "LARGE", out url) && !string.IsNullOrEmpty(url))
            return ExtractIdFromUrl(url);

        // Try squareImage
        if (element.TryGetProperty("squareImage", out var squareImage) &&
            squareImage.ValueKind == JsonValueKind.String)
            return squareImage.GetString();

        // Try image
        if (element.TryGetProperty("image", out var image) &&
            image.ValueKind == JsonValueKind.String)
            return image.GetString();

        // Try cover
        if (element.TryGetProperty("cover", out var cover) &&
            cover.ValueKind == JsonValueKind.String)
            return cover.GetString();

        return null;
    }

    /// <summary>
    /// Extracts the image ID from a full Tidal image URL.
    /// </summary>
    private static string? ExtractIdFromUrl(string url)
    {
        // URL format: https://resources.tidal.com/images/{id}/{width}x{height}.jpg
        if (string.IsNullOrEmpty(url))
            return null;

        var parts = url.Split('/');
        if (parts.Length >= 2)
        {
            // Get the part before the dimensions
            var idPart = parts[^2];
            return idPart.Replace('/', '-');
        }

        return null;
    }
}
