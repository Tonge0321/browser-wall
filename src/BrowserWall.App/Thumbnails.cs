using BrowserWall.Core.Layout;

namespace BrowserWall.App;

/// <summary>Registers, positions and unregisters DWM live thumbnails.</summary>
internal sealed class Thumbnails : IDisposable
{
    private readonly IntPtr _wall;
    private readonly Dictionary<long, IntPtr> _handles = new();

    public Thumbnails(IntPtr wall)
    {
        _wall = wall;
    }

    /// <summary>Registers a thumbnail for a managed window; not repeated if already registered. Returns false on failure.</summary>
    public bool Register(long windowId)
    {
        if (_handles.ContainsKey(windowId))
        {
            return true;
        }

        int hr = NativeMethods.DwmRegisterThumbnail(_wall, new IntPtr(windowId), out IntPtr thumb);
        if (hr != 0 || thumb == IntPtr.Zero)
        {
            return false;
        }

        _handles[windowId] = thumb;
        return true;
    }

    /// <summary>Unregisters a window's thumbnail.</summary>
    public void Unregister(long windowId)
    {
        if (_handles.Remove(windowId, out IntPtr thumb))
        {
            NativeMethods.DwmUnregisterThumbnail(thumb);
        }
    }

    /// <summary>
    /// Fits the thumbnail proportionally and centered into <paramref name="area"/> (wall client coordinates, physical pixels).
    /// Hides the thumbnail when the source size is unknown. <paramref name="opacity"/>: 255 is opaque; 128 marks a cell being dragged.
    /// </summary>
    public void Place(long windowId, CellRect area, byte opacity = 255)
    {
        if (!_handles.TryGetValue(windowId, out IntPtr thumb))
        {
            return;
        }

        int hr = NativeMethods.DwmQueryThumbnailSourceSize(thumb, out NativeMethods.SIZE size);
        CellRect target = hr == 0 ? CellContent.FitCentered(size.cx, size.cy, area) : new CellRect(area.X, area.Y, 0, 0);

        var props = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = NativeMethods.DWM_TNP_RECTDESTINATION | NativeMethods.DWM_TNP_VISIBLE
                | NativeMethods.DWM_TNP_OPACITY | NativeMethods.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new NativeMethods.RECT
            {
                Left = target.X,
                Top = target.Y,
                Right = target.Right,
                Bottom = target.Bottom,
            },
            opacity = opacity,
            fVisible = target.Width > 0 && target.Height > 0,
            fSourceClientAreaOnly = false,
        };
        NativeMethods.DwmUpdateThumbnailProperties(thumb, ref props);
    }

    public void Dispose()
    {
        foreach (IntPtr thumb in _handles.Values)
        {
            NativeMethods.DwmUnregisterThumbnail(thumb);
        }

        _handles.Clear();
    }
}
