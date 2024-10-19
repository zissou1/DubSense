using System.Configuration;
using System.Data;
using System.Windows;
using Forms = System.Windows.Forms; // Alias for System.Windows.Forms

namespace DubSense
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application // Fully qualified to resolve ambiguity
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Optional: Initialize any global resources or settings here
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            // Removed Forms.Application.Exit();
        }
    }
}
