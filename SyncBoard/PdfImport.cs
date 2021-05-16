using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
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
                var page = pdfDoc.GetPage(i);
                RenderImage(page, i, (uint)(i + mainPage.pdfSiteCounter));
            }

            mainPage.pdfSiteCounter += (uint)pdfDoc.PageCount;
        }

        public async void RenderImage(PdfPage page, uint i, uint pageNr)
        {
            BitmapImage image = new BitmapImage();

            //var page = pdfDoc.GetPage(i);

            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions() { DestinationHeight = (uint)(MainPage.PRINT_RECTANGLE_HEIGHT * MainPage.PDF_IMPORT_ZOOM) });
                using (InMemoryRandomAccessStream test = new InMemoryRandomAccessStream())
                {
                    await this.ConvertImageToJpegAsync(stream, test);
                    await image.SetSourceAsync(test);
                }
                /*InMemoryRandomAccessStream test = new InMemoryRandomAccessStream();
                await this.ConvertImageToJpegAsync(stream, test);
                await image.SetSourceAsync(test);*/
                //await image.SetSourceAsync(stream);

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

        private async Task<InMemoryRandomAccessStream> ConvertImageToJpegAsync(InMemoryRandomAccessStream imageStream, InMemoryRandomAccessStream output)
        {
            //you can use WinRTXamlToolkit StorageItemExtensions.GetSizeAsync to get file size (if you already plugged this nuget in)
            //var sourceFileProperties = await sourceFile.GetBasicPropertiesAsync();
            //var fileSize = sourceFileProperties.Size;
            //var imageStream = await sourceFile.OpenReadAsync();
            //BitmapImage img;

            //var imageWriteableStream = new InMemoryRandomAccessStream();
            var imageWriteableStream = output;
            //Stopwatch stopwatch = new Stopwatch();
            //stopwatch.Start();
            /*using (imageStream)
            {*/
                var decoder = await BitmapDecoder.CreateAsync(imageStream);
                var pixelData = await decoder.GetPixelDataAsync();
                var detachedPixelData = pixelData.DetachPixelData();
                pixelData = null;
                //0.85d
                double jpegImageQuality = 0.2d;
                //since we're using MvvmCross, we're outputing diagnostic info to MvxTrace, you can use System.Diagnostics.Debug.WriteLine instead
                //Mvx.TaggedTrace(MvxTraceLevel.Diagnostic, "ImageService", $"Source image size: {fileSize}, trying Q={jpegImageQuality}");
                //var imageWriteableStream = await outputFile.OpenAsync(FileAccessMode.ReadWrite);
                
                ulong jpegImageSize = 0;
                /*using (imageWriteableStream)
                {*/
                    var propertySet = new BitmapPropertySet();
                    var qualityValue = new BitmapTypedValue(jpegImageQuality, Windows.Foundation.PropertyType.Single);
                    propertySet.Add("ImageQuality", qualityValue);
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, imageWriteableStream, propertySet);
                    //key thing here is to use decoder.OrientedPixelWidth and decoder.OrientedPixelHeight otherwise you will get garbled image on devices on some photos with orientation in metadata
                    encoder.SetPixelData(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, decoder.DpiX, decoder.DpiY, detachedPixelData);
                    await encoder.FlushAsync();
                    await imageWriteableStream.FlushAsync();
                    //jpegImageSize = imageWriteableStream.Size;
                //}
               // Mvx.TaggedTrace(MvxTraceLevel.Diagnostic, "ImageService", $"Final image size now: {jpegImageSize}");
            //
            //stopwatch.Stop();
           // Mvx.TaggedTrace(MvxTraceLevel.Diagnostic, "ImageService", $"Time spent optimizing image: {stopwatch.Elapsed}");
            return imageWriteableStream;
        }
    }
}
