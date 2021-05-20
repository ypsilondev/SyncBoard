using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Graphics.Printing;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace SyncBoard.Utiles
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


        public async static void PrintCanvas(InkCanvas inkCanvas, Panel imports, Canvas printCanvas)
        {
            var _printHelper = new PrintHelper(printCanvas);
            // Canvas PrintCanvas = new Canvas();

            // Calculate amount of required pages
            int pageCount = (int)inkCanvas.ActualHeight / MainPage.PAGE_HEIGHT + 1;

            // Clear the print-canvas
            printCanvas.Children.Clear();


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
            // Paint background PFDs
            foreach (Viewbox pdfSite in imports.Children)
            {
                int page = (int)(pdfSite.Translation.Y / MainPage.PAGE_HEIGHT);
                int pageOffset = 0;

                while (pdfSite.Translation.Y + pdfSite.Height >= MainPage.PAGE_HEIGHT * (page + pageOffset))
                {
                    BitmapImage img2 = (BitmapImage)((Image)pdfSite.Child).Source;
                    Viewbox v = PdfImport.CreateBackgroundImageViewbox(img2, 0);
                    pagePanels[page + pageOffset].Children.Add(v);
                    pageOffset++;
                }
            }

            // Paint the strokes to the pages          
            foreach (var stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
            {
                int page = (int)(stroke.BoundingRect.Top / MainPage.PAGE_HEIGHT);
                int pageOffset = 0;

                while (stroke.BoundingRect.Bottom > MainPage.PAGE_HEIGHT * (page + pageOffset))
                {
                    var polyLine = PrintUtil.CreatePolyLineFromStroke(stroke);
                    PrintUtil.TranslatePolyLineToPage(polyLine, page + pageOffset);
                    pagePanels[page + pageOffset].Children.Add(polyLine);
                    pageOffset++;
                }
            }

            // Add all pages to the output (except blanks)
            for (int i = 0; i < pageCount; i++)
            {
                if (pagePanels[i].Children.Count > 0 || pageCount <= 1)
                {
                    // PrintCanvas.Children.Add(pagePanels[i]);
                    _printHelper.AddFrameworkElementToPrint(pagePanels[i]);
                }
            }
            pagePanels = null; // Remove reference for performance reasons.

            // Open print-GUI
            try
            {

                var printHelperOptions = new PrintHelperOptions();
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.Orientation);
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.CustomPageRanges);
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.Duplex);

                //PrintTaskOptions options = new PrintTaskOptions();
                //options.MediaSize = PrintMediaSize.IsoA4;
                //printHelperOptions.AddDisplayOption();

                printHelperOptions.Orientation = PrintOrientation.Portrait;
                printHelperOptions.PrintQuality = PrintQuality.High;



                // var _printHelper = new PrintHelper(PrintCanvas);
                _printHelper.OnPrintSucceeded += PrintSucceded;
                _printHelper.OnPrintFailed += PrintFailed;


                await _printHelper.ShowPrintUIAsync("SyncBoard Print", printHelperOptions, false);
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

        private static void PrintFailed()
        {
            MainPage.Instance.DisplayMessage("Printing failed. Please try again ¯\\_(ツ)_/¯");
        }




    }
}
