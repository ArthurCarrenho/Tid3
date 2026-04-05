using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace TidalUi3.Helpers;

public enum DiffKind { Added, Removed, Changed }

public class DiffEntry
{
    public string Path { get; init; } = "";
    public DiffKind Kind { get; init; }
    public string? ValueA { get; init; }
    public string? ValueB { get; init; }

    public string Symbol => Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => "~" };

    public SolidColorBrush KindBrush => Kind switch
    {
        DiffKind.Added   => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 222, 128)),
        DiffKind.Removed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)),
        _                => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 251, 191, 36)),
    };

    public string Display => Kind switch
    {
        DiffKind.Added   => $"+  {Path}  =  {ValueB}",
        DiffKind.Removed => $"−  {Path}  =  {ValueA}",
        _                => $"~  {Path}  :  {ValueA}  →  {ValueB}",
    };
}

public static class JsonDiff
{
    public static List<DiffEntry> Compute(JsonElement a, JsonElement b, string path = "root")
    {
        var results = new List<DiffEntry>();
        Compare(a, b, path, results);
        return results;
    }

    private static void Compare(JsonElement a, JsonElement b, string path, List<DiffEntry> out_)
    {
        if (a.ValueKind != b.ValueKind)
        {
            out_.Add(new DiffEntry { Path = path, Kind = DiffKind.Changed, ValueA = a.ToString(), ValueB = b.ToString() });
            return;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var keysA = new HashSet<string>();
                foreach (var p in a.EnumerateObject()) keysA.Add(p.Name);
                var keysB = new HashSet<string>();
                foreach (var p in b.EnumerateObject()) keysB.Add(p.Name);

                foreach (var key in keysA)
                {
                    var child = $"{path}.{key}";
                    if (!keysB.Contains(key))
                        out_.Add(new DiffEntry { Path = child, Kind = DiffKind.Removed, ValueA = a.GetProperty(key).ToString() });
                    else
                        Compare(a.GetProperty(key), b.GetProperty(key), child, out_);
                }
                foreach (var key in keysB)
                    if (!keysA.Contains(key))
                        out_.Add(new DiffEntry { Path = $"{path}.{key}", Kind = DiffKind.Added, ValueB = b.GetProperty(key).ToString() });
                break;
            }
            case JsonValueKind.Array:
            {
                int lenA = a.GetArrayLength(), lenB = b.GetArrayLength(), len = Math.Min(lenA, lenB);
                for (int i = 0; i < len; i++) Compare(a[i], b[i], $"{path}[{i}]", out_);
                for (int i = len; i < lenA; i++) out_.Add(new DiffEntry { Path = $"{path}[{i}]", Kind = DiffKind.Removed, ValueA = a[i].ToString() });
                for (int i = len; i < lenB; i++) out_.Add(new DiffEntry { Path = $"{path}[{i}]", Kind = DiffKind.Added, ValueB = b[i].ToString() });
                break;
            }
            default:
            {
                var sa = a.ToString(); var sb = b.ToString();
                if (sa != sb) out_.Add(new DiffEntry { Path = path, Kind = DiffKind.Changed, ValueA = sa, ValueB = sb });
                break;
            }
        }
    }
}
