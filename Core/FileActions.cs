using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Core;

/// <summary>Clipboard, shell and icon helpers for the file shelf.</summary>
public static class FileActions
{
    /// <summary>Marks drags that start on the shelf so the island doesn't take them back.</summary>
    public const string ShelfDragFormat = "DynamicIsland.Shelf";

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" };

    /// <summary>Puts the files themselves on the clipboard (paste into Explorer, chat apps...). Text paste gives the paths.</summary>
    public static bool CopyFiles(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return false;
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        var data = new DataObject();
        data.SetFileDropList(list);
        data.SetText(string.Join(Environment.NewLine, paths));
        return SetClipboard(data);
    }

    /// <summary>Puts the full paths on the clipboard as text, one per line.</summary>
    public static bool CopyPaths(IReadOnlyCollection<string> paths) =>
        paths.Count > 0 && SetClipboard(new DataObject(DataFormats.UnicodeText, string.Join(Environment.NewLine, paths)));

    /// <summary>Data for dragging shelf items out to other apps.</summary>
    public static DataObject CreateDragData(IReadOnlyCollection<string> paths)
    {
        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        data.SetData(ShelfDragFormat, true);
        return data;
    }

    public static bool Open(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Opens Explorer with the item selected.</summary>
    public static bool Reveal(string path) =>
        Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static bool SetClipboard(DataObject data)
    {
        // Another app holding the clipboard open makes this fail briefly (CLIPBRD_E_CANT_OPEN).
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(40);
            }
            catch (COMException)
            {
                return false;
            }
        }
    }

    /// <summary>A small thumbnail for images, otherwise the shell icon (works for folders too). Null if neither loads.</summary>
    public static ImageSource? GetIcon(string path)
    {
        if (File.Exists(path) && ImageExtensions.Contains(Path.GetExtension(path)))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.DecodePixelWidth = 96;
                bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException
                                           or ArgumentException or InvalidOperationException)
            {
                // Fall back to the shell icon.
            }
        }
        return GetShellIcon(path);
    }

    private static ImageSource? GetShellIcon(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) == IntPtr.Zero
            || info.hIcon == IntPtr.Zero)
            return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
}
