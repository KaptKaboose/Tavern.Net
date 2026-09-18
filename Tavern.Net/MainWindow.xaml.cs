using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tavern.Net.ViewModels;

namespace Tavern.Net
{
    public partial class MainWindow : Window
    {
        // Recolors the native title bar (icon/text/minimize-maximize-close area) to match the
        // app's own dark palette instead of the OS accent color — a DWM attribute, Windows 11
        // (build 22000+) only. DwmSetWindowAttribute just returns a failure HRESULT on older
        // Windows, which this ignores rather than reimplementing the whole title bar (custom
        // WindowChrome) just to recolor it.
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            SourceInitialized += OnSourceInitialized;

            // Shortcuts are handled at the window (PreviewKeyDown), but a key press is routed to
            // whichever element has keyboard focus first — so if focus is stranded on something
            // that's no longer usable, the press never reaches this window at all. Re-check whenever
            // the window is activated or clicked (the natural "I'm about to press a key" moments).
            Activated += (_, _) => EnsureKeyboardFocusIsUsable();
            PreviewMouseDown += (_, _) => EnsureKeyboardFocusIsUsable();
        }

        /// <summary>
        /// If the element holding keyboard focus was removed from this window, hidden, or disabled
        /// (a Button on an overlay that just closed, a view that was swapped out, ...), keyboard
        /// input has nowhere valid to go — every shortcut appears dead until the window is minimized
        /// and restored, which is exactly what re-establishes focus by hand. Park focus on the
        /// window itself instead.
        /// </summary>
        private void EnsureKeyboardFocusIsUsable()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            var usable = focused is Visual visual
                && visual.IsDescendantOf(this)
                && (focused as UIElement)?.IsVisible != false
                && (focused as UIElement)?.IsEnabled != false;

            if (!usable)
            {
                Keyboard.Focus(this);
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;

            // COLORREF is 0x00BBGGRR (reversed from the usual 0xRRGGBB) — #1A202C (this app's own
            // dark background) and #F7FAFC (its usual light text) for the caption background/text.
            var captionColor = 0x2C201A;
            var textColor = 0xFCFAF7;
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
        }

        private void ToggleFullScreen()
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        /// <summary>
        /// Routes key presses to whichever view-model is currently active. Handled at the tunnel
        /// phase (PreviewKeyDown), before a focused Button's own bubble-phase handling can treat
        /// Space/Enter as "click me" — see IKeyboardShortcutHandler for why that ordering matters.
        /// Skipped entirely while a TextBox has focus (typing a deck/save name, pasting a decklist,
        /// ...) so letters like 'S' or 'N' type normally instead of firing a board shortcut.
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            if (Keyboard.FocusedElement is TextBox focusedTextBox)
            {
                if (focusedTextBox.IsVisible)
                {
                    return;
                }

                // A TextBox that kept keyboard focus after its overlay closed (e.g. Save Game or
                // Generate's search box dismissed by clicking the scrim, which is deliberately not
                // focusable and so never steals focus) is invisible but would still swallow every
                // shortcut — the "N (or any key) does nothing until something else gets clicked" bug.
                // Nobody can be typing into it, so drop the stale focus and carry on.
                Keyboard.ClearFocus();
            }

            if (DataContext is MainViewModel { CurrentView: IKeyboardShortcutHandler handler } && handler.HandleKey(e.Key))
            {
                e.Handled = true;
            }
        }
    }
}