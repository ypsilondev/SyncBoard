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
    public sealed partial class SettingsPage : UserControl
    {

        public static int PDF_IMPORT_ZOOM { get; set; } = 1;

        public static int BACKGROUND_DENSITY_DELTA { get; private set; } = 20;

        public SettingsPage()
        {
            this.InitializeComponent();

            this.pdfQualitySelector.ValueChanged += PdfQualityChanged;
            this.backgroundDenisitySelector.ValueChanged += BackgroundDensityChanged;
        }

        private void PdfQualityChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            PDF_IMPORT_ZOOM = (int)args.NewValue;
        }

        private void BackgroundDensityChanged(object sender, RangeBaseValueChangedEventArgs args)
        {
            BACKGROUND_DENSITY_DELTA = (int)args.NewValue;
            MainPage.Instance.CreateBackground(true);
        }
    }
}
