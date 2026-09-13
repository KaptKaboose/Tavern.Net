using System.Windows;
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
    }
}