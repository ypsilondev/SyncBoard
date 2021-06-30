using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

// Die Elementvorlage "Benutzersteuerelement" wird unter https://go.microsoft.com/fwlink/?LinkId=234236 dokumentiert.

namespace SyncBoard.UserControls
{
    public sealed partial class SettingsPage : UserControl
    {

        public static int PDF_IMPORT_ZOOM { get; set; } = 1;

        public static int BACKGROUND_DENSITY_DELTA { get; private set; } = 20;

        public static BackgroundStyle BACKGROUND_STYLE { get; private set; } = BackgroundStyle.BOXES;

        public SettingsPage()
        {
            this.InitializeComponent();

            // this.serverSelector.TextChanged += ServerChanged;

            this.pdfQualitySelector.ValueChanged += PdfQualityChanged;
            this.backgroundDenisitySelector.ValueChanged += BackgroundDensityChanged;
        }

        private void ConfirmServerChange(object sender, RoutedEventArgs e)
        {
            string server = serverSelector.Text.ToString().ToLower();
            System.Diagnostics.Debug.WriteLine("Switched to host: " + server);
            Network.SetServer(server);
            if (MainPage.Instance != null)
            {
                MainPage.Instance.InitSocket();
            }
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

        private void BackgroundStyleChanged(object sender, RoutedEventArgs e)
        {
            RadioButton btn = (RadioButton)sender;
            if(btn != null)
            {
                string selected = btn.Tag.ToString();

                switch (selected)
                {
                    case "backgroundBoxes":
                        BACKGROUND_STYLE = BackgroundStyle.BOXES;
                        this.CreateBackground();
                        break;
                    case "backgroundLines":
                        BACKGROUND_STYLE = BackgroundStyle.LINES;
                        this.CreateBackground();
                        break;
                }
            }
        }

        private void CreateBackground()
        {
            if(MainPage.Instance != null)
            {
                MainPage.Instance.CreateBackground(true);
            }
        }
    }

    public enum BackgroundStyle
    {
        LINES, BOXES
    }
}
