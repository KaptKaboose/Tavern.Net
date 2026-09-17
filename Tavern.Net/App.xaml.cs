using System.Configuration;
using System.Data;
using System.Windows;

namespace Tavern.Net
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // As early as possible — every storage service reads AppInstanceSlot.Id in its own
            // constructor, so this has to happen before any of them (MainViewModel's fields) exist.
            AppInstanceSlot.Claim();
        }
    }

}
