using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

        /// <summary>
        /// Routes key presses to whichever view-model is currently active. Handled at the tunnel
        /// phase (PreviewKeyDown), before a focused Button's own bubble-phase handling can treat
        /// Space/Enter as "click me" — see IKeyboardShortcutHandler for why that ordering matters.
        /// Skipped entirely while a TextBox has focus (typing a deck/save name, pasting a decklist,
        /// ...) so letters like 'S' or 'N' type normally instead of firing a board shortcut.
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            if (DataContext is MainViewModel { CurrentView: IKeyboardShortcutHandler handler } && handler.HandleKey(e.Key))
            {
                e.Handled = true;
            }
        }
    }
}