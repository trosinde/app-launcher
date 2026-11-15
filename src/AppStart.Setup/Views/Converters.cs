using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppStart.Setup.Views;

/// <summary>true → Visible, false → Collapsed; Invert swaps them.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>null or empty string → Collapsed, otherwise Visible; Invert swaps them.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null || value is string { Length: 0 }) ^ Invert ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// EXE path → its first icon (32 px) via ExtractIconEx and Imaging.CreateBitmapSourceFromHIcon
/// (no System.Drawing). Null when the file has no icon; the view then shows a coloured placeholder.
/// </summary>
public sealed partial class ExeIconConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } path ? Cache.GetOrAdd(path, Load) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static ImageSource? Load(string exePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        nint large = 0;
        try
        {
            if (ExtractIconExW(exePath, 0, ref large, 0, 1) == 0 || large == 0)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or ExternalException)
        {
            return null;
        }
        finally
        {
            if (large != 0)
            {
                DestroyIcon(large);
            }
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string lpszFile, int nIconIndex, ref nint phiconLarge, nint phiconSmall, uint nIcons);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint hIcon);
}
