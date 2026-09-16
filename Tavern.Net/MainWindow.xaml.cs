using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tavern.Net.ViewModels;

namespace Tavern.Net
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
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