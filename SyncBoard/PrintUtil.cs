using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Printing;
using Windows.Storage.Streams;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace SyncBoard
{
    class PrintUtil
    {


        public static Polyline CreatePolyLineFromStroke(InkStroke stroke)
        {
            var polyLine = new Polyline();
            polyLine.Stroke = new SolidColorBrush(stroke.DrawingAttributes.Color);
            if (stroke.DrawingAttributes.Kind.Equals(InkDrawingAttributesKind.Pencil))
            {
                polyLine.StrokeDashArray = new DoubleCollection();
                polyLine.StrokeDashArray.Add(1);
                polyLine.StrokeDashArray.Add(0.3);
            }
            if (stroke.DrawingAttributes.DrawAsHighlighter)
            {
                polyLine.Opacity = 0.5;
            }
            polyLine.StrokeThickness = stroke.DrawingAttributes.Size.Height;
            var points = new PointCollection();
            foreach (var point in stroke.GetInkPoints())
            {
                points.Add(point.Position);
            }
            polyLine.Points = points;

            return polyLine;
        }

        public static Polyline TranslatePolyLineToPage(Polyline polyLine, int page)
        {
            polyLine.Translation = new Vector3(0, -MainPage.PAGE_HEIGHT * page, 0);
            return polyLine;
        }


        public async static void PrintCanvas(InkCanvas inkCanvas, Grid imports, Canvas PrintCanvas)
        {
            // Canvas PrintCanvas = new Canvas();

            // Calculate amount of required pages
            int pageCount = (int)inkCanvas.ActualHeight / MainPage.PAGE_HEIGHT + 1;

            // Clear the print-canvas
            PrintCanvas.Children.Clear();

            System.Diagnostics.Debug.WriteLine("Creating page panels");

            // Setup the required pages
            List<Panel> pagePanels = new List<Panel>();
            for (int i = 0; i < pageCount; i++)
            {
                Panel panel = new ItemsStackPanel();
                panel.Height = MainPage.PRINT_RECTANGLE_HEIGHT;
                panel.Width = MainPage.PRINT_RECTANGLE_WIDTH;
                panel.Margin = new Thickness(0, 0, 0, 0);
                pagePanels.Add(panel);
            }
            System.Diagnostics.Debug.WriteLine("Creating background pdf");
            // Paint background PFDs
            foreach (Viewbox pdfSite in imports.Children)
            {
                int page = (int)(pdfSite.Translation.Y / MainPage.PAGE_HEIGHT);
                int pageOffset = 0;

                while (pdfSite.Translation.Y + pdfSite.Height >= MainPage.PAGE_HEIGHT * (page + pageOffset))
                {
                    // System.Diagnostics.Debug.WriteLine(pdfSite.Translation.Y + pdfSite.Height + "_" + MainPage.PAGE_HEIGHT * (page + pageOffset));
                    BitmapImage img2 = (BitmapImage)((Image)pdfSite.Child).Source;
                    Viewbox v = PdfImport.CreateBackgroundImageViewbox(img2, 0);
                    img2 = null;
                    pagePanels[page + pageOffset].Children.Add(v);
                    pageOffset++;
                }
            }

            System.Diagnostics.Debug.WriteLine("Creating strokes");
            // Paint the strokes to the pages          
            foreach (var stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
            {
                int page = (int)(stroke.BoundingRect.Top / MainPage.PAGE_HEIGHT);
                int pageOffset = 0;

                while (stroke.BoundingRect.Bottom > MainPage.PAGE_HEIGHT * (page + pageOffset))
                {
                    var polyLine = PrintUtil.CreatePolyLineFromStroke(stroke);
                    //polyLine2.Translation = new Vector3(0, -PAGE_HEIGHT * (page + 1), 0);
                    PrintUtil.TranslatePolyLineToPage(polyLine, page + pageOffset);
                    pagePanels[page + pageOffset].Children.Add(polyLine);
                    pageOffset++;
                }
            }

            System.Diagnostics.Debug.WriteLine("Adding panels to output");
            // Add all pages to the output (except blanks)
            for (int i = 0; i < pageCount; i++)
            {
                if (pagePanels[i].Children.Count > 0)
                {
                    PrintCanvas.Children.Add(pagePanels[i]);
                }
            }
            pagePanels = null;

            // Open print-GUI
            try
            {
                System.Diagnostics.Debug.WriteLine("Setup printhelper");
                var _printHelper = new PrintHelper(PrintCanvas);
                var printHelperOptions = new PrintHelperOptions();
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.Orientation);
                printHelperOptions.Orientation = PrintOrientation.Portrait;
                printHelperOptions.PrintQuality = PrintQuality.High;


                _printHelper.OnPrintSucceeded += PrintSucceded;

                System.Diagnostics.Debug.WriteLine("Open Print-dialog");
                _printHelper.ShowPrintUIAsync("SyncBoard Print", printHelperOptions, true);
            }
            catch (Exception ignored)
            {
                System.Diagnostics.Debug.WriteLine(ignored.Message);
                System.Diagnostics.Debug.WriteLine(ignored.StackTrace);
            }
        }

        private static void PrintSucceded()
        {
            System.Diagnostics.Debug.WriteLine("PRINT DONE");
            MainPage.Instance.DisplayMessage("PRINT DONE!");
        }




    }
}
