using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace SyncBoard
{
    class PdfImport
    {

        private MainPage mainPage;
        private PdfDocument pdfDoc;
        private Grid imports;

        public PdfImport(PdfDocument pdfDoc, Grid imports, MainPage mainPage)
        {
            this.pdfDoc = pdfDoc;
            this.imports = imports;
            this.mainPage = mainPage;
        }

        public void Load()
        {
            mainPage.expandBoard(0, (int)(pdfDoc.PageCount + 1) * MainPage.PRINT_RECTANGLE_HEIGHT);

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                RenderImage(pdfDoc, i, (uint)(i + mainPage.pdfSiteCounter));
            }

            mainPage.pdfSiteCounter += (uint)pdfDoc.PageCount;
        }

        public async void RenderImage(PdfDocument pdfDoc, uint i, uint pageNr)
        {
            BitmapImage image = new BitmapImage();

            var page = pdfDoc.GetPage(i);

            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions() { DestinationHeight = (uint)(MainPage.PRINT_RECTANGLE_HEIGHT * MainPage.PDF_IMPORT_ZOOM) });
                await image.SetSourceAsync(stream);
            }
            Viewbox site = CreateBackgroundImageViewbox(image, pageNr);
            this.imports.Children.Add(site);
        }

        public static Viewbox CreateBackgroundImageViewbox(BitmapImage image, uint page)
        {
            // FIXME weird issue when mixing Hoch und Querformat
            double imageSiteRatio = (double)image.PixelHeight / image.PixelWidth;

            int imgRenderWidth;
            int imgRenderHeight;

            if (imageSiteRatio <= MainPage.PAGE_SITE_RATIO)
            {
                imgRenderWidth = MainPage.PRINT_RECTANGLE_WIDTH;
                imgRenderHeight = (int)(imageSiteRatio * MainPage.PRINT_RECTANGLE_WIDTH);
            }
            else
            {
                imgRenderWidth = (int)(imageSiteRatio * MainPage.PRINT_RECTANGLE_HEIGHT);
                imgRenderHeight = MainPage.PRINT_RECTANGLE_HEIGHT;
            }

            Viewbox site = new Viewbox()
            {
                Child = new Image()
                {
                    Source = image,
                    Width = imgRenderWidth,
                    Height = imgRenderHeight
                },
                Width = imgRenderWidth,
                Height = imgRenderHeight
            };

            site.Translation = new Vector3((int)(MainPage.PRINT_RECTANGLE_WIDTH - imgRenderWidth) / 2, page * MainPage.PRINT_RECTANGLE_HEIGHT + (int)(MainPage.PRINT_RECTANGLE_HEIGHT - imgRenderHeight) / 2, 0);
            return site;
        }
    }
}
