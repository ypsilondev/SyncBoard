using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// Die Elementvorlage "Benutzersteuerelement" wird unter https://go.microsoft.com/fwlink/?LinkId=234236 dokumentiert.

namespace SyncBoard.UserControls
{
    public sealed partial class NavBar : UserControl
    {
        public NavBar()
        {
            this.InitializeComponent();
            navBar.ItemInvoked += NavView_ItemInvoked;
            // navBar.Opacity = 0;
            
        }

        public void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                // ContentFrame.Navigate(typeof(SettingsPage));
                System.Diagnostics.Debug.WriteLine("Settings called!");
                ToggleVisibility(MainPage.Instance.GetSettingsPage());
                // frame.SourcePageType = typeof(SettingsPage);
            }
            else
            {
                // find NavigationViewItem with Content that equals InvokedItem
                var item = sender.MenuItems.OfType<NavigationViewItem>().First(x => (string)x.Content == (string)args.InvokedItem);
                NavView_Navigate(item as NavigationViewItem);
            }
        }
        private void NavView_Navigate(NavigationViewItem item)
        {
            System.Diagnostics.Debug.WriteLine(item.Tag);
            switch (item.Tag)
            {
                /*case "board":
                    // navBar.Visibility = Visibility.Collapsed;
                    ToggleVisibility(MainPage.Instance.GetSettingsPage());
                    break;*/
                /*case "print":
                    MainPage.Instance.Printer_Click(null, null);
                    break;*/

                case "fullscreen":
                    MainPage.Instance.ToggleFullscreen();
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine("No action found for nav-event.");
                    break;

                    /*case "apps":
                        ContentFrame.Navigate(typeof(AppsPage));
                        break;
                        */

            }
        }

        private void ToggleVisibility(UserControl userControl)
        {
            userControl.Visibility = userControl.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
