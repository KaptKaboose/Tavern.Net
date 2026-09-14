using System.Windows;
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
        /// </summary>
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel { CurrentView: IKeyboardShortcutHandler handler } && handler.HandleKey(e.Key))
            {
                e.Handled = true;
            }
        }
    }
}