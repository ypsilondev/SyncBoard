using SyncBoard.UserControls;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace SyncBoard.Utiles
{
    class PdfImport
    {

        private MainPage mainPage;
        private PdfDocument pdfDoc;
        private Panel imports;

        public PdfImport(PdfDocument pdfDoc, Panel imports, MainPage mainPage)
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
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions() { DestinationHeight = (uint)(MainPage.PRINT_RECTANGLE_HEIGHT * SettingsPage.PDF_IMPORT_ZOOM) });
                using (InMemoryRandomAccessStream test = new InMemoryRandomAccessStream())
                {
                    // Optional image compression for improved network efficiency
                    //await this.ConvertImageToJpegAsync(stream, test);
                    // await image.SetSourceAsync(test);
                    await image.SetSourceAsync(stream);
                }
            }
            /*using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions() { DestinationHeight = (uint)(MainPage.PRINT_RECTANGLE_HEIGHT * PDF_IMPORT_ZOOM) });
                using (InMemoryRandomAccessStream test = new InMemoryRandomAccessStream())
                {
                    await this.ConvertImageToJpegAsync(stream, test);
                    // await image.SetSourceAsync(test);
                    //if (i == 1)
                    //{
                        var writeableBitmap = new WriteableBitmap(image.PixelWidth, image.PixelHeight);
                        await writeableBitmap.SetSourceAsync(test);
                        await SaveImageToFile("import_"+i+".jpeg", writeableBitmap);
                   // }
                }
            }*/
            Viewbox site = CreateBackgroundImageViewbox(image, pageNr);
            this.imports.Children.Add(site);


        }

        public static async Task<string> SaveImageToFile(string fileName, WriteableBitmap workBmp)
        {
            try
            {
                var writeStream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, writeStream);

                // saving to a straight 32 bpp PNG, the dpiX and dpiY values are irrelevant, but also cannot be zero. 
                // We will use the default "since-the-beginning-of-time" 96 dpi.
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)workBmp.PixelWidth, (uint)workBmp.PixelHeight, 96, 96, workBmp.PixelBuffer.ToArray());

                await encoder.FlushAsync();

                writeStream.Seek(0);

                System.Diagnostics.Debug.WriteLine("penner");

                // var dl = await DownloadsFolder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                StorageFolder folder = Windows.Storage.ApplicationData.Current.LocalFolder;

                System.Diagnostics.Debug.WriteLine(folder.Path);
                var dl = await folder.CreateFileAsync(fileName, Windows.Storage.CreationCollisionOption.ReplaceExisting);

                var imgFileOut = await dl.OpenStreamForWriteAsync();

                var fileProxyStream = writeStream.AsStreamForRead();

                await fileProxyStream.CopyToAsync(imgFileOut);
                await imgFileOut.FlushAsync();

                fileProxyStream.Dispose();
                imgFileOut.Dispose();

                var xc = Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList;

                return xc.Add(dl);
            }
            catch (Exception ex)
            {
                return null;
            }
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
            var imageWriteableStream = output;
            using (imageStream)
            {
                var decoder = await BitmapDecoder.CreateAsync(imageStream);
                var detachedPixelData = (await decoder.GetPixelDataAsync()).DetachPixelData();

                double jpegImageQuality = 0.05d;

                var propertySet = new BitmapPropertySet();
                var qualityValue = new BitmapTypedValue(jpegImageQuality, Windows.Foundation.PropertyType.Single);
                propertySet.Add("ImageQuality", qualityValue);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, imageWriteableStream, propertySet);
                //key thing here is to use decoder.OrientedPixelWidth and decoder.OrientedPixelHeight otherwise you will get garbled image on devices on some photos with orientation in metadata
                encoder.SetPixelData(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, decoder.DpiX, decoder.DpiY, detachedPixelData);
                await encoder.FlushAsync();
                await imageWriteableStream.FlushAsync();
            }

            return imageWriteableStream;
        }
    }
}
