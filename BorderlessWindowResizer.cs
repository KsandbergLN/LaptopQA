using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace LaptopQA.Windows;

/// <summary>
/// Adds native resize behavior to a transparent, borderless WPF window while
/// preserving a fixed design surface's aspect ratio when the window is restored.
/// </summary>
public static class BorderlessWindowResizer
{
    private const int ResizeZoneDip = 14;

    public static void Attach(Window window, double designWidth, double designHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (designWidth <= 0 || designHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(designWidth), "Design dimensions must be positive.");
        }

        _ = new Attachment(window, designWidth / designHeight);
    }

    private sealed class Attachment
    {
        private const int GwlStyle = -16;
        private const int WsThickFrame = 0x00040000;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpFrameChanged = 0x0020;
        private const int WmNcHitTest = 0x0084;
        private const int WmSizing = 0x0214;
        private const int WmSysCommand = 0x0112;
        private const int ScSize = 0xF000;
        private const int HtClient = 1;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;
        private const int WmszLeft = 1;
        private const int WmszRight = 2;
        private const int WmszTop = 3;
        private const int WmszTopLeft = 4;
        private const int WmszTopRight = 5;
        private const int WmszBottom = 6;
        private const int WmszBottomLeft = 7;
        private const int WmszBottomRight = 8;

        private readonly Window _window;
        private readonly double _aspectRatio;
        private HwndSource? _source;

        public Attachment(Window window, double aspectRatio)
        {
            _window = window;
            _aspectRatio = aspectRatio;
            _window.SourceInitialized += Window_SourceInitialized;
            _window.Closed += Window_Closed;
            _window.PreviewMouseMove += Window_PreviewMouseMove;
            _window.PreviewMouseLeftButtonDown += Window_PreviewMouseLeftButtonDown;
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            _source = (HwndSource?)PresentationSource.FromVisual(_window);
            if (_source is null)
            {
                return;
            }

            nint hwnd = _source.Handle;
            nint style = GetWindowLongPtr(hwnd, GwlStyle);
            SetWindowLongPtr(hwnd, GwlStyle, new nint(style.ToInt64() | WsThickFrame));
            _source.AddHook(WindowProc);
            SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            if (_source is not null)
            {
                _source.RemoveHook(WindowProc);
                _source = null;
            }

            _window.SourceInitialized -= Window_SourceInitialized;
            _window.Closed -= Window_Closed;
            _window.PreviewMouseMove -= Window_PreviewMouseMove;
            _window.PreviewMouseLeftButtonDown -= Window_PreviewMouseLeftButtonDown;
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_window.WindowState != WindowState.Normal)
            {
                _window.Cursor = null;
                return;
            }

            _window.Cursor = CursorFor(HitTestDip(e.GetPosition(_window)));
        }

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_source is null || _window.WindowState != WindowState.Normal)
            {
                return;
            }

            int hit = HitTestDip(e.GetPosition(_window));
            if (hit == HtClient)
            {
                return;
            }

            // WPF fallback for transparent client areas. Native non-client hits use
            // the same SC_SIZE path automatically before this event is raised.
            SendMessage(_source.Handle, WmSysCommand, new nint(ScSize + ToSizingEdge(hit)), nint.Zero);
            e.Handled = true;
        }

        private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == WmNcHitTest && _window.WindowState == WindowState.Normal)
            {
                int hit = HitTestPixels(hwnd, lParam);
                if (hit != HtClient)
                {
                    handled = true;
                    return new nint(hit);
                }
            }
            else if (message == WmSizing && _window.WindowState == WindowState.Normal)
            {
                EnforceAspectRatio(hwnd, ToInt32(wParam), lParam);
                handled = true;
            }

            return nint.Zero;
        }

        private int HitTestPixels(nint hwnd, nint lParam)
        {
            if (!GetWindowRect(hwnd, out RectPx rect))
            {
                return HtClient;
            }

            int x = SignedLowWord(lParam);
            int y = SignedHighWord(lParam);
            int zone = DipToPixels(hwnd, ResizeZoneDip);
            bool left = x >= rect.Left && x < rect.Left + zone;
            bool right = x < rect.Right && x >= rect.Right - zone;
            bool top = y >= rect.Top && y < rect.Top + zone;
            bool bottom = y < rect.Bottom && y >= rect.Bottom - zone;
            return HitTest(left, right, top, bottom);
        }

        private int HitTestDip(Point point)
        {
            double zone = ResizeZoneDip;
            bool left = point.X >= 0 && point.X < zone;
            bool right = point.X < _window.ActualWidth && point.X >= _window.ActualWidth - zone;
            bool top = point.Y >= 0 && point.Y < zone;
            bool bottom = point.Y < _window.ActualHeight && point.Y >= _window.ActualHeight - zone;
            return HitTest(left, right, top, bottom);
        }

        private static int HitTest(bool left, bool right, bool top, bool bottom)
        {
            if (top && left) return HtTopLeft;
            if (top && right) return HtTopRight;
            if (bottom && left) return HtBottomLeft;
            if (bottom && right) return HtBottomRight;
            if (left) return HtLeft;
            if (right) return HtRight;
            if (top) return HtTop;
            return bottom ? HtBottom : HtClient;
        }

        private void EnforceAspectRatio(nint hwnd, int sizingEdge, nint lParam)
        {
            if (lParam == nint.Zero)
            {
                return;
            }

            RectPx rect = Marshal.PtrToStructure<RectPx>(lParam);
            int minWidth = Math.Max(1, DipToPixels(hwnd, _window.MinWidth));
            int minHeight = Math.Max(1, DipToPixels(hwnd, _window.MinHeight));
            int width = Math.Max(1, rect.Right - rect.Left);
            int height = Math.Max(1, rect.Bottom - rect.Top);

            switch (sizingEdge)
            {
                case WmszLeft:
                case WmszRight:
                    SetHorizontalResize(ref rect, sizingEdge, width, minWidth, minHeight);
                    break;
                case WmszTop:
                case WmszBottom:
                    SetVerticalResize(ref rect, sizingEdge, height, minWidth, minHeight);
                    break;
                case WmszTopLeft:
                case WmszTopRight:
                case WmszBottomLeft:
                case WmszBottomRight:
                    SetCornerResize(ref rect, sizingEdge, width, height, minWidth, minHeight);
                    break;
            }

            Marshal.StructureToPtr(rect, lParam, false);
        }

        private void SetHorizontalResize(ref RectPx rect, int sizingEdge, int requestedWidth, int minWidth, int minHeight)
        {
            int width = Math.Max(requestedWidth, Math.Max(minWidth, (int)Math.Ceiling(minHeight * _aspectRatio)));
            int height = Math.Max(minHeight, (int)Math.Round(width / _aspectRatio));
            int centerY = rect.Top + ((rect.Bottom - rect.Top) / 2);
            rect.Bottom = centerY + (height / 2);
            rect.Top = rect.Bottom - height;
            if (sizingEdge == WmszLeft)
            {
                rect.Left = rect.Right - width;
            }
            else
            {
                rect.Right = rect.Left + width;
            }
        }

        private void SetVerticalResize(ref RectPx rect, int sizingEdge, int requestedHeight, int minWidth, int minHeight)
        {
            int height = Math.Max(requestedHeight, Math.Max(minHeight, (int)Math.Ceiling(minWidth / _aspectRatio)));
            int width = Math.Max(minWidth, (int)Math.Round(height * _aspectRatio));
            int centerX = rect.Left + ((rect.Right - rect.Left) / 2);
            rect.Right = centerX + (width / 2);
            rect.Left = rect.Right - width;
            if (sizingEdge == WmszTop)
            {
                rect.Top = rect.Bottom - height;
            }
            else
            {
                rect.Bottom = rect.Top + height;
            }
        }

        private void SetCornerResize(ref RectPx rect, int edge, int requestedWidth, int requestedHeight, int minWidth, int minHeight)
        {
            int widthFromWidth = Math.Max(requestedWidth, Math.Max(minWidth, (int)Math.Ceiling(minHeight * _aspectRatio)));
            int heightFromWidth = Math.Max(minHeight, (int)Math.Round(widthFromWidth / _aspectRatio));
            int heightFromHeight = Math.Max(requestedHeight, Math.Max(minHeight, (int)Math.Ceiling(minWidth / _aspectRatio)));
            int widthFromHeight = Math.Max(minWidth, (int)Math.Round(heightFromHeight * _aspectRatio));

            int width;
            int height;
            if (Math.Abs(heightFromWidth - requestedHeight) <= Math.Abs(widthFromHeight - requestedWidth))
            {
                width = widthFromWidth;
                height = heightFromWidth;
            }
            else
            {
                width = widthFromHeight;
                height = heightFromHeight;
            }

            bool anchorRight = edge is WmszTopLeft or WmszBottomLeft;
            bool anchorBottom = edge is WmszTopLeft or WmszTopRight;
            if (anchorRight)
            {
                rect.Left = rect.Right - width;
            }
            else
            {
                rect.Right = rect.Left + width;
            }

            if (anchorBottom)
            {
                rect.Top = rect.Bottom - height;
            }
            else
            {
                rect.Bottom = rect.Top + height;
            }
        }

        private static Cursor? CursorFor(int hit) => hit switch
        {
            HtLeft or HtRight => Cursors.SizeWE,
            HtTop or HtBottom => Cursors.SizeNS,
            HtTopLeft or HtBottomRight => Cursors.SizeNWSE,
            HtTopRight or HtBottomLeft => Cursors.SizeNESW,
            _ => null
        };

        private static int ToSizingEdge(int hit) => hit switch
        {
            HtLeft => WmszLeft,
            HtRight => WmszRight,
            HtTop => WmszTop,
            HtTopLeft => WmszTopLeft,
            HtTopRight => WmszTopRight,
            HtBottom => WmszBottom,
            HtBottomLeft => WmszBottomLeft,
            HtBottomRight => WmszBottomRight,
            _ => WmszRight
        };

        private static int DipToPixels(nint hwnd, double dip) => Math.Max(1, (int)Math.Round(dip * GetDpiForWindowSafe(hwnd) / 96.0));

        private static uint GetDpiForWindowSafe(nint hwnd)
        {
            try
            {
                return GetDpiForWindow(hwnd);
            }
            catch (EntryPointNotFoundException)
            {
                return 96;
            }
        }

        private static int SignedLowWord(nint value) => unchecked((short)(value.ToInt64() & 0xffff));

        private static int SignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xffff));

        private static int ToInt32(nint value) => unchecked((int)value.ToInt64());

        [StructLayout(LayoutKind.Sequential)]
        private struct RectPx
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(nint hWnd, out RectPx lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(nint hWnd);
    }
}
