using Microsoft.Toolkit.Uwp.Helpers;
using Newtonsoft.Json.Linq;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input.Inking;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

// Die Elementvorlage "Leere Seite" wird unter https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x407 dokumentiert.

namespace SyncBoard
{
    /// <summary>
    /// Eine leere Seite, die eigenständig verwendet oder zu der innerhalb eines Rahmens navigiert werden kann.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private static int EXPAND_MARGIN = 600;

        private Dictionary<Guid, InkStroke> syncedStrokes = new Dictionary<Guid, InkStroke>();
        private Dictionary<InkStroke, Guid> reverseStrokes = new Dictionary<InkStroke, Guid>();

        private SocketIO socket;
        private Boolean offlineMode = false;

        private String roomCode = "";

        private static int PAGE_HEIGHT = 1123, PAGE_WIDTH = 794,

            PRINT_RECTANGLE_WIDTH = 794,
            PRINT_RECTANGLE_HEIGHT = 1123,
            AMOUNT_INITIAL_RECTANGLES = 2,

            BACKGROUND_DENSITY_DELTA = 20,

            BORDER_EXPANSION = 1128,

            PDF_IMPORT_ZOOM = 1;

        private static double PAGE_SITE_RATIO = (double)PRINT_RECTANGLE_HEIGHT / PRINT_RECTANGLE_WIDTH;

        private uint rectangleCounter = 0, pdfSiteCounter = 0;

        public MainPage()
        {
            this.InitializeComponent();

            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.FullScreen;

            // Set supported inking device types.
            inkCanvas.InkPresenter.InputDeviceTypes =
                Windows.UI.Core.CoreInputDeviceTypes.Mouse |
                Windows.UI.Core.CoreInputDeviceTypes.Pen;

            // Set initial ink stroke attributes.
            InkDrawingAttributes drawingAttributes = new InkDrawingAttributes();

            drawingAttributes.Color =
                Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? Windows.UI.Colors.White
                : Windows.UI.Colors.Black;
            drawingAttributes.IgnorePressure = false;

            drawingAttributes.FitToCurve = true;
            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(drawingAttributes);

            inkCanvas.InkPresenter.StrokesErased += InkPresenter_StrokesErased;
            inkCanvas.InkPresenter.StrokesCollected += InkPresenter_Drawed;




            // Init background:
            InitializePrintSiteBackground();
            CreateBackground();

            // Network and sync:
            InitSocket();
        }

        private void InkPresenter_StrokesErased(InkPresenter sender, InkStrokesErasedEventArgs args)
        {
            Guid[] eraseIds = new Guid[args.Strokes.Count];
            for (int i = 0; i < args.Strokes.ToArray().Length; i++)
            {
                eraseIds[i] = reverseStrokes.GetValueOrDefault(args.Strokes.ToArray()[i]);
            }
            CallErasement(eraseIds);
        }

        private void InkPresenter_Drawed(InkPresenter presenter, InkStrokesCollectedEventArgs args)
        {
            DrawStrokes(args.Strokes.ToList());
        }

        private async void InitSocket()
        {
            socket = new SocketIO(Network.URL, new SocketIOOptions
            {
                EIO = 4,
                Reconnection = true,
                ReconnectionDelay = 1000,
                AllowedRetryFirstConnection = true
            });

            HandleSocketConnection();

            try
            {
                await socket.ConnectAsync();
                ListenIncome();
            }
            catch (System.Net.WebSockets.WebSocketException e)
            {
                SetOfflineMode(true);
            }
        }

        private async void HandleSocketConnection()
        {
            socket.OnConnected += ((sender, args) =>
            {
                SetOfflineMode(false);

                if (roomCode != "")
                {
                    socket.EmitAsync("cmd", "{\"action\": \"join\", \"payload\": \"" + roomCode + "\"}");
                }
            });

            socket.OnDisconnected += ((sender, args) =>
            {
                SetOfflineMode(true);
            });
        }

        private async void ListenIncome()
        {
            socket.On("sync", (data) =>
            {
                JArray updateStrokes = data.GetValue<JArray>();

                foreach (var strokePointArray in updateStrokes)
                {
                    JObject stroke = (JObject)strokePointArray;
                    InkStroke c = ParseFromJSON(stroke);

                    syncedStrokes.Add(Guid.Parse(stroke.Value<String>("guid")), c);
                    reverseStrokes.Add(c, Guid.Parse(stroke.Value<String>("guid")));

                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                        RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            inkCanvas.InkPresenter.StrokeContainer.AddStroke(c);
                        });
                }
            });

            socket.On("cmd", (data) =>
            {
                JObject dataJson = data.GetValue<JObject>();

                if (dataJson.ContainsKey("token"))
                {
                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                            RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                roomCodeBox.Text = dataJson.Value<String>("token");
                                roomCodeBox.Visibility = Visibility.Visible;
                                roomCode = dataJson.Value<String>("token");
                            });
                }
                else if (dataJson.ContainsKey("success"))
                {
                    connectRoom();
                }
                else if (dataJson.ContainsKey("action"))
                {
                    if (dataJson.Value<String>("action") == "joined")
                    {
                        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                            RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                userJoinedText.Text = "User JOINED";
                                userJoinedText.Visibility = Visibility.Visible;
                            });

                        Thread.Sleep(2000);

                        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                            RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                userJoinedText.Visibility = Visibility.Collapsed;
                            });
                    }
                    else if (dataJson.Value<String>("action") == "sendBoard")
                    {
                        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                            RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                List<InkStroke> strokes = new List<InkStroke>();
                                foreach (InkStroke stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
                                {
                                    strokes.Add(stroke);
                                }

                                SyncData(strokes, "init-sync");
                            });
                    }
                    else if (dataJson.Value<String>("action") == "left")
                    {
                        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                            RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                userJoinedText.Text = "User LEFT";
                                userJoinedText.Visibility = Visibility.Visible;
                            });

                        Thread.Sleep(2000);

                        _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                            RunAsync(CoreDispatcherPriority.Normal, () =>
                            {
                                userJoinedText.Visibility = Visibility.Collapsed;
                            });
                    }
                    else if (dataJson.Value<String>("action") == "clearRoom")
                    {
                        ClearRoom();
                    }
                }

            });

            socket.On("erase", (data) =>
            {
                Guid[] toEraseIds = data.GetValue<Guid[]>();
                foreach (Guid eraseId in toEraseIds)
                {
                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       if (syncedStrokes.GetValueOrDefault(eraseId) != null)
                       {
                           var inkPoints = syncedStrokes.GetValueOrDefault(eraseId).GetInkPoints().ToArray();
                           for (int i = 1; i < inkPoints.Length; i++)
                           {
                               Point p = new Point(inkPoints[i - 1].Position.X, inkPoints[i - 1].Position.Y);
                               Point pEnd = new Point(inkPoints[i].Position.X, inkPoints[i].Position.Y);
                               var selectLineRect = inkCanvas.InkPresenter.StrokeContainer.SelectWithLine(p, pEnd);
                               var deleteRect = inkCanvas.InkPresenter.StrokeContainer.DeleteSelected();
                           }
                       }
                   });
                }
            });
        }

        private void SyncData(List<InkStroke> toSync, String channel)
        {
            if (toSync.Count == 0) return;
            JArray json = new JArray();

            toSync.ForEach(syncStroke =>
            {
                json.Add(CreateJSONStrokeFrom(syncStroke));
            });

            socket.EmitAsync(channel, json);
        }

        private void CallErasement(Guid[] erasedIds)
        {
            /*JArray ids = new JArray(erasedIds);

            JObject obj = new JObject();
            obj.Add("action", "erase");
            obj.Add("data", ids);*/
            socket.EmitAsync("erase", erasedIds);
        }

        private void expandBoard(bool bottom)
        {
            if (bottom)
            {
                //outputGrid.Height += BORDER_EXPANSION;
                //inkCanvas.Height += BORDER_EXPANSION;
                expandBoard(0, BORDER_EXPANSION);
            }
            else
            {
                expandBoard(BORDER_EXPANSION, 0);
                //outputGrid.Width += BORDER_EXPANSION;
                //inkCanvas.Width += BORDER_EXPANSION;
            }

            /*if (inkCanvas.Height >= rectangleCounter * PRINT_RECTANGLE_HEIGHT)
            {
                for (int i = 0; i <= ((inkCanvas.Height - rectangleCounter * PRINT_RECTANGLE_HEIGHT) / BORDER_EXPANSION) + 1; i++)
                {
                    CreateNewPrintSiteBackground();
                }
            }
            CreateBackground();*/
        }

        private void expandBoard(int x, int y)
        {
            outputGrid.Height += y;
            inkCanvas.Height += y;

            outputGrid.Width += x;
            inkCanvas.Width += x;

            while (inkCanvas.Height >= rectangleCounter * PRINT_RECTANGLE_HEIGHT)
            {
                CreateNewPrintSiteBackground();
            }

            /*if (inkCanvas.Height >= rectangleCounter * PRINT_RECTANGLE_HEIGHT)
            {
                for (int i = 0; i <= ((inkCanvas.Height - rectangleCounter * PRINT_RECTANGLE_HEIGHT) / BORDER_EXPANSION) + 1; i++)
                {
                    CreateNewPrintSiteBackground();
                }
            }*/
            CreateBackground();
        }

        private void expandBoard(float offset)
        {
            outputGrid.Height = offset + BORDER_EXPANSION;
            inkCanvas.Height = offset + BORDER_EXPANSION;

            outputGrid.Width = offset + BORDER_EXPANSION;
            inkCanvas.Width = offset + BORDER_EXPANSION;

            if (inkCanvas.Height >= rectangleCounter * PRINT_RECTANGLE_HEIGHT)
            {
                for (int i = 0; i <= ((inkCanvas.Height - rectangleCounter * PRINT_RECTANGLE_HEIGHT) / BORDER_EXPANSION) + 1; i++)
                {
                    CreateNewPrintSiteBackground();
                }
            }
            CreateBackground();
        }

        private void SetOfflineMode(bool set)
        {
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       offlineMode = set;
                       offlineModeToggleButton.IsChecked = set;

                       if (!set && socket.Connected)
                       {
                           socket.EmitAsync("cmd", "{\"action\": \"create\"}");
                       }
                       else
                       {
                           roomCodeBox.Visibility = Visibility.Collapsed;
                           roomCode = "";
                       }
                   });
        }

        private void connectRoom()
        {
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       inkCanvas.InkPresenter.StrokeContainer.Clear();
                   });
            // hier
        }

        // Call offline mode
        private void offlineModeToggleButton_Checked(object sender, RoutedEventArgs e)
        {

        }

        // Connect room
        private void connectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!offlineMode && socket.Connected)
            {
                socket.EmitAsync("cmd", "{\"action\": \"join\", \"payload\": \"" + roomCodeBox.Text + "\"}");
            }
        }

        // Room-Code Checkbox
        private void roomCodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (roomCodeBox.Text.Length == 4 && roomCodeBox.Text != roomCode)
            {
                connectButton.IsEnabled = true;
            }
        }

        // New room
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ClearRoom();
            socket.DisconnectAsync();
            roomCode = "";
            InitSocket();
        }

        // Export board to GIF
        private async void exportBoard(object sender, RoutedEventArgs e)
        {
            // Get all strokes on the InkCanvas.
            IReadOnlyList<InkStroke> currentStrokes = inkCanvas.InkPresenter.StrokeContainer.GetStrokes();

            // Strokes present on ink canvas.
            if (currentStrokes.Count > 0)
            {
                // Let users choose their ink file using a file picker.
                // Initialize the picker.
                Windows.Storage.Pickers.FileSavePicker savePicker =
                    new Windows.Storage.Pickers.FileSavePicker();
                savePicker.SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add(
                    "GIF with embedded ISF",
                    new List<string>() { ".gif" });
                savePicker.DefaultFileExtension = ".gif";
                savePicker.SuggestedFileName = "InkSample";

                // Show the file picker.
                Windows.Storage.StorageFile file =
                    await savePicker.PickSaveFileAsync();
                // When chosen, picker returns a reference to the selected file.
                if (file != null)
                {
                    // Prevent updates to the file until updates are 
                    // finalized with call to CompleteUpdatesAsync.
                    Windows.Storage.CachedFileManager.DeferUpdates(file);
                    // Open a file stream for writing.
                    IRandomAccessStream stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                    // Write the ink strokes to the output stream.
                    using (IOutputStream outputStream = stream.GetOutputStreamAt(0))
                    {
                        await inkCanvas.InkPresenter.StrokeContainer.SaveAsync(outputStream);
                        await outputStream.FlushAsync();
                    }
                    stream.Dispose();

                    // Finalize write so other apps can update file.
                    Windows.Storage.Provider.FileUpdateStatus status =
                        await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                    if (status == Windows.Storage.Provider.FileUpdateStatus.Complete)
                    {
                        // File saved.
                    }
                    else
                    {
                        // File couldn't be saved.
                    }
                }
                // User selects Cancel and picker returns null.
                else
                {
                    // Operation cancelled.
                }
            }
        }

        // Import board from GIF
        private async void importBoard(object sender, RoutedEventArgs e)
        {
            // Let users choose their ink file using a file picker.
            // Initialize the picker.
            Windows.Storage.Pickers.FileOpenPicker openPicker =
                new Windows.Storage.Pickers.FileOpenPicker();
            openPicker.SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".gif");
            // Show the file picker.
            Windows.Storage.StorageFile file = await openPicker.PickSingleFileAsync();
            // User selects a file and picker returns a reference to the selected file.
            if (file != null)
            {
                // Open a file stream for reading.
                IRandomAccessStream stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                // Read from file.
                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    await inkCanvas.InkPresenter.StrokeContainer.LoadAsync(inputStream);
                }
                stream.Dispose();
            }
            // User selects Cancel and picker returns null.
            else
            {
                // Operation cancelled.
            }
        }

        // Enter fullscreen
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if (ApplicationView.GetForCurrentView().IsFullScreenMode)
            {
                ApplicationView.GetForCurrentView().ExitFullScreenMode();
            }
            else
            {
                ApplicationView.GetForCurrentView().TryEnterFullScreenMode();
            }

            fullscreenIcon.IsChecked = ApplicationView.GetForCurrentView().IsFullScreenMode;
        }

        private Polyline CreatePolyLineFromStroke(InkStroke stroke)
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

        private Polyline TranslatePolyLineToPage(Polyline polyLine, int page)
        {
            polyLine.Translation = new Vector3(0, -PAGE_HEIGHT * page, 0);
            return polyLine;
        }

        // Print PDF
        private async void Printer_Click(object sender, RoutedEventArgs e)
        {
            // Calculate amount of required pages
            int pageCount = (int)inkCanvas.ActualHeight / PAGE_HEIGHT + 1;

            // Clear the print-canvas
            PrintCanvas.Children.Clear();

            // Setup the required pages
            List<Panel> pagePanels = new List<Panel>();
            for (int i = 0; i < pageCount; i++)
            {
                Panel panel = new ItemsStackPanel();
                panel.Height = PRINT_RECTANGLE_HEIGHT;
                panel.Width = PRINT_RECTANGLE_WIDTH;
                panel.Margin = new Thickness(0, 0, 0, 0);
                pagePanels.Add(panel);
            }

            // Paint background PFDs
            foreach (Viewbox pdfSite in imports.Children)
            {
                int page = (int)(pdfSite.Translation.Y / PAGE_HEIGHT);
                int pageOffset = 0;



                while (pdfSite.Translation.Y + pdfSite.Height >= PAGE_HEIGHT * (page + pageOffset))
                {
                    System.Diagnostics.Debug.WriteLine(pdfSite.Translation.Y + pdfSite.Height + "_" + PAGE_HEIGHT * (page + pageOffset));
                    Image img = (Image)pdfSite.Child;
                    BitmapImage img2 = (BitmapImage)img.Source;
                    /*Viewbox v = new Viewbox()
                    {
                        Child = new Image()
                        {
                            Source = img.Source,
                            Margin = new Thickness(0, 0, 0, 0),

                            MaxWidth = PRINT_RECTANGLE_WIDTH,
                            MaxHeight = PRINT_RECTANGLE_HEIGHT
                        },
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };*/



                    Viewbox v = CreateBackgroundImageViewbox(img2, 0);

                    //v.VerticalAlignment = VerticalAlignment.Center;
                    //v.HorizontalAlignment = HorizontalAlignment.Center;

                    //v.Width = PRINT_RECTANGLE_WIDTH;
                    //v.Height = PRINT_RECTANGLE_HEIGHT;
                    //v.Margin = new Thickness(0,0,0,0);
                    //v.Translation = new Vector3((int)(PRINT_RECTANGLE_WIDTH - v.Width)/2, (int)(PRINT_RECTANGLE_HEIGHT - v.Height) / 2, 0);

                    pagePanels[page + pageOffset].Children.Add(v);
                    pageOffset++;
                }
            }

            // Paint the strokes to the pages          
            foreach (var stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
            {
                int page = (int)(stroke.BoundingRect.Top / PAGE_HEIGHT);
                int pageOffset = 0;

                while (stroke.BoundingRect.Bottom > PAGE_HEIGHT * (page + pageOffset))
                {
                    var polyLine = this.CreatePolyLineFromStroke(stroke);
                    //polyLine2.Translation = new Vector3(0, -PAGE_HEIGHT * (page + 1), 0);
                    this.TranslatePolyLineToPage(polyLine, page + pageOffset);
                    pagePanels[page + pageOffset].Children.Add(polyLine);
                    pageOffset++;
                }
            }

            // Add all pages to the output (except blanks)
            for (int i = 0; i < pageCount; i++)
            {
                if (pagePanels[i].Children.Count > 0)
                {
                    PrintCanvas.Children.Add(pagePanels[i]);
                }
            }

            // Open print-GUI
            try
            {
                var _printHelper = new PrintHelper(PrintCanvas);
                var printHelperOptions = new PrintHelperOptions();
                printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.Orientation);
                printHelperOptions.Orientation = PrintOrientation.Portrait;
                printHelperOptions.PrintQuality = PrintQuality.High;

                _printHelper.OnPrintSucceeded += PrintSucceded;

                await _printHelper.ShowPrintUIAsync("SyncBoard Print", printHelperOptions, true);
            }
            catch (Exception ignored)
            {

            }
        }

        private void PrintSucceded()
        {
            System.Diagnostics.Debug.WriteLine("PRINT DONE");
            this.DisplayMessage("PRINT DONE!");
        }



        // Background rectangles to indicate where the print area is
        private void InitializePrintSiteBackground()
        {
            for (int i = 0; i < AMOUNT_INITIAL_RECTANGLES; i++)
            {
                CreateNewPrintSiteBackground();
            }
        }

        private void CreateNewPrintSiteBackground()
        {
            Rectangle rectangle = new Rectangle();
            rectangle.Width = PRINT_RECTANGLE_WIDTH;
            rectangle.Height = PRINT_RECTANGLE_HEIGHT;
            rectangle.Margin = new Thickness(0, PRINT_RECTANGLE_HEIGHT * rectangleCounter, 0, 0);
            rectangle.Fill = new SolidColorBrush(Color.FromArgb(5, 255, 255, 255));
            rectangle.Stroke = new SolidColorBrush(Color.FromArgb(255, 81, 81, 81));
            rectangle.VerticalAlignment = VerticalAlignment.Top;
            rectangle.HorizontalAlignment = HorizontalAlignment.Left;

            printBackgrounds.Children.Add(rectangle);
            rectangleCounter++;
        }

        private void TogglePrintSiteBackgrounds(object sender, RoutedEventArgs e)
        {
            if (togglePrintBackground.IsChecked == true)
            {
                printBackgrounds.Visibility = Visibility.Visible;
            }
            else
            {
                printBackgrounds.Visibility = Visibility.Collapsed;
            }
        }


        // Background lines
        private void CreateBackground()
        {
            // Horizontal lines
            int start = (int)Math.Max(inkCanvas.Height - BORDER_EXPANSION, 0);
            if (inkCanvas.Height - BORDER_EXPANSION < BORDER_EXPANSION)
            {
                start = 0;
            }
            for (int i = start; i <= inkCanvas.Height; i += BACKGROUND_DENSITY_DELTA)
            {
                Line line = new Line();
                line.X1 = 0;
                line.X2 = Window.Current.Bounds.Width;
                line.Y1 = i;
                line.Y2 = i;

                line.Stroke = new SolidColorBrush(Color.FromArgb(50, 21, 21, 21));
                line.StrokeThickness = 1.0;
                background.Children.Add(line);
            }

            // Vertical lines
            for (int i = 0; i <= Window.Current.Bounds.Width; i += BACKGROUND_DENSITY_DELTA)
            {
                Line line = new Line();
                line.X1 = i;
                line.X2 = i;
                line.Y1 = start;
                line.Y2 = inkCanvas.Height;

                line.Stroke = new SolidColorBrush(Color.FromArgb(50, 21, 21, 21));
                line.StrokeThickness = 1.0;
                background.Children.Add(line);
            }
        }

        private void ToggleBackgroundLines(object sender, RoutedEventArgs e)
        {
            if (backgroundToggle.IsChecked == true)
            {
                background.Visibility = Visibility.Visible;
                CreateBackground();
            }
            else
            {
                background.Visibility = Visibility.Collapsed;
            }
        }


        // Import PDF
        private async void ImportPDF(object sender, RoutedEventArgs e)
        {
            try
            {
                Windows.Storage.Pickers.FileOpenPicker openPicker =
                new Windows.Storage.Pickers.FileOpenPicker();
                openPicker.SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                openPicker.FileTypeFilter.Add(".pdf");

                // Show the file picker.
                Windows.Storage.StorageFile f = await openPicker.PickSingleFileAsync();
                PdfDocument doc = await PdfDocument.LoadFromFileAsync(f);

                Load(doc);
            }
            catch (Exception ignored)
            {

            }
        }

        private async void Load(PdfDocument pdfDoc)
        {
            this.expandBoard(0, (int)(pdfDoc.PageCount + 1) * PRINT_RECTANGLE_HEIGHT);

            for (uint i = 0; i < pdfDoc.PageCount; i++)
            {
                /*BitmapImage image = new BitmapImage();

                var page = pdfDoc.GetPage(i);


                using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
                {
                    await page.RenderToStreamAsync(stream, new PdfPageRenderOptions() { DestinationHeight = (uint)PRINT_RECTANGLE_HEIGHT*2 });
                    await image.SetSourceAsync(stream);
                }
                Viewbox site = CreateBackgroundImageViewbox(image, i + pdfSiteCounter);
                imports.Children.Add(site);*/
                RenderImage(pdfDoc, i, (uint)(i + pdfSiteCounter));
            }

            pdfSiteCounter += (uint)pdfDoc.PageCount;
        }

        private async void RenderImage(PdfDocument pdfDoc, uint i, uint pageNr)
        {
            BitmapImage image = new BitmapImage();

            var page = pdfDoc.GetPage(i);

            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions() { DestinationHeight = (uint)(PRINT_RECTANGLE_HEIGHT * PDF_IMPORT_ZOOM) });
                await image.SetSourceAsync(stream);
            }
            Viewbox site = CreateBackgroundImageViewbox(image, pageNr);
            imports.Children.Add(site);
        }

        private Viewbox CreateBackgroundImageViewbox(BitmapImage image, uint page)
        {
            // FIXME weird issue when mixing Hoch und Querformat
            double imageSiteRatio = (double)image.PixelHeight / image.PixelWidth;

            int imgRenderWidth;
            int imgRenderHeight;

            if (imageSiteRatio <= PAGE_SITE_RATIO)
            {
                imgRenderWidth = PRINT_RECTANGLE_WIDTH;
                imgRenderHeight = (int)(imageSiteRatio * PRINT_RECTANGLE_WIDTH);
            }
            else
            {
                imgRenderWidth = (int)(imageSiteRatio * PRINT_RECTANGLE_HEIGHT);
                imgRenderHeight = PRINT_RECTANGLE_HEIGHT;
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

            site.Translation = new Vector3((int)(PRINT_RECTANGLE_WIDTH - imgRenderWidth) / 2, page * PRINT_RECTANGLE_HEIGHT + (int)(PRINT_RECTANGLE_HEIGHT - imgRenderHeight) / 2, 0);
            return site;
        }

        // Create JSON export
        private async void CreateJSONExport(object sender, RoutedEventArgs e)
        {
            JArray board = new JArray();

            foreach (Guid key in syncedStrokes.Keys)
            {
                board.Add(CreateJSONStrokeFrom(syncedStrokes.GetValueOrDefault(key)));
            }

            try
            {
                Windows.Storage.Pickers.FileSavePicker savePicker =
                    new Windows.Storage.Pickers.FileSavePicker();
                savePicker.SuggestedStartLocation =
                    Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add(
                    "Syncboard JSON",
                    new List<string>() { ".json" });
                savePicker.DefaultFileExtension = ".json";
                savePicker.SuggestedFileName = "export";

                StorageFile sampleFile = await savePicker.PickSaveFileAsync();

                await Windows.Storage.FileIO.WriteTextAsync(sampleFile, board.ToString());
            }
            catch (Exception ignored)
            {

            }
        }

        private JObject CreateJSONStrokeFrom(InkStroke syncStroke)
        {
            JObject ö = new JObject();

            JObject color = new JObject();
            color.Add("A", syncStroke.DrawingAttributes.Color.A);
            color.Add("R", syncStroke.DrawingAttributes.Color.R);
            color.Add("G", syncStroke.DrawingAttributes.Color.G);
            color.Add("B", syncStroke.DrawingAttributes.Color.B);

            ö.Add("color", color);
            ö.Add("guid", reverseStrokes.GetValueOrDefault(syncStroke));

            // Send the tool-size
            JObject size = new JObject();
            size.Add("w", syncStroke.DrawingAttributes.Size.Width);
            size.Add("h", syncStroke.DrawingAttributes.Size.Height);

            JObject toolInfo = new JObject();
            toolInfo.Add("size", size);
            toolInfo.Add("marker", syncStroke.DrawingAttributes.DrawAsHighlighter);
            toolInfo.Add("pencil", syncStroke.DrawingAttributes.Kind.Equals(InkDrawingAttributesKind.Pencil));

            ö.Add("tool", toolInfo);

            JArray oneStrokePoints = new JArray();
            foreach (var strokePoint in syncStroke.GetInkPoints())
            {
                JObject o = new JObject();
                o.Add("x", strokePoint.Position.X);
                o.Add("y", strokePoint.Position.Y);
                o.Add("p", strokePoint.Pressure);

                oneStrokePoints.Add(o);
            }

            ö.Add("points", oneStrokePoints);

            return ö;
        }

        // Load from JSON file
        private async void LoadJSON(object sender, RoutedEventArgs e)
        {
            Windows.Storage.Pickers.FileOpenPicker openPicker =
            new Windows.Storage.Pickers.FileOpenPicker();
            openPicker.SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".json");

            // Show the file picker.
            Windows.Storage.StorageFile f = await openPicker.PickSingleFileAsync();
            String text = await Windows.Storage.FileIO.ReadTextAsync(f);

            JArray board = JArray.Parse(text);
            ClearRoom();

            List<InkStroke> syncingStrokes = new List<InkStroke>();
            foreach (JObject obj in board)
            {
                InkStroke c = ParseFromJSON(obj);

                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        inkCanvas.InkPresenter.StrokeContainer.AddStroke(c);
                    });

                syncingStrokes.Add(c);
            }

            DrawStrokes(syncingStrokes);
        }

        private InkStroke ParseFromJSON(JObject stroke)
        {
            List<InkPoint> inkPoints = new List<InkPoint>();

            foreach (var point in stroke.Value<JArray>("points"))
            {
                JObject o = (JObject)point;
                Point p = new Point(o.Value<float>("x"), o.Value<float>("y"));
                InkPoint ip = new InkPoint(p, o.Value<float>("p"));
                inkPoints.Add(ip);

                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                        RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            if (o.Value<float>("y") >= inkCanvas.Height - EXPAND_MARGIN)
                            {
                                expandBoard(o.Value<float>("y"));
                            }
                            else if (o.Value<float>("x") >= inkCanvas.Width - EXPAND_MARGIN)
                            {
                                expandBoard(o.Value<float>("x"));
                            }
                        });
            }

            InkStrokeBuilder b = new InkStrokeBuilder();
            InkDrawingAttributes da = new InkDrawingAttributes();

            // Pressure
            JObject toolInfo = stroke.Value<JObject>("tool");
            if (toolInfo != null)
            {
                if (toolInfo.Value<Boolean>("pencil"))
                {
                    da = InkDrawingAttributes.CreateForPencil();
                }
                else
                {
                    da.DrawAsHighlighter = toolInfo.Value<Boolean>("marker");
                }

                da.Size = new Size((double)toolInfo.Value<JObject>("size").GetValue("w"),
                            (double)toolInfo.Value<JObject>("size").GetValue("h"));
            }

            // Color
            da.Color = Windows.UI.ColorHelper.FromArgb(
                (byte)stroke.Value<JObject>("color").GetValue("A"),
                (byte)stroke.Value<JObject>("color").GetValue("R"),
                (byte)stroke.Value<JObject>("color").GetValue("G"),
                (byte)stroke.Value<JObject>("color").GetValue("B")
            );
            da.IgnorePressure = false;
            da.FitToCurve = true;

            b.SetDefaultDrawingAttributes(da);
            InkStroke c = b.CreateStrokeFromInkPoints(inkPoints, Matrix3x2.Identity);

            return c;
        }

        private void ClearRoom()
        {
            pdfSiteCounter = 0;
            syncedStrokes.Clear();
            reverseStrokes.Clear();

            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       inkCanvas.InkPresenter.StrokeContainer.Clear();
                       imports.Children.Clear();
                   });
        }

        private void EmitRoomClear()
        {
            JObject emit = new JObject();
            emit.Add("action", "clearRoom");
            socket.EmitAsync("cmd", emit.ToString());
        }

        private void DrawStrokes(List<InkStroke> strokes)
        {
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       List<InkStroke> toSync = new List<InkStroke>();

                       foreach (var stroke in strokes)
                       {
                           foreach (var point in stroke.GetInkPoints())
                           {
                               if (point.Position.Y >= inkCanvas.Height - EXPAND_MARGIN)
                               {
                                   expandBoard(true);
                               }
                               else if (point.Position.X >= inkCanvas.Width - EXPAND_MARGIN)
                               {
                                   expandBoard(false);
                               }
                           }

                           Guid guid = Guid.NewGuid();
                           syncedStrokes.Add(guid, stroke);
                           reverseStrokes.Add(stroke, guid);

                           toSync.Add(stroke);
                       }

                       if (!offlineMode) SyncData(toSync, "sync");
                   });
        }

        private async void DisplayMessage(String msg)
        {

            var messageDialog = new MessageDialog(msg);

            messageDialog.Commands.Add(new UICommand(
                "Close"));

            // Set the command that will be invoked by default
            messageDialog.DefaultCommandIndex = 0;

            // Set the command to be invoked when escape is pressed
            messageDialog.CancelCommandIndex = 1;

            // Show the message dialog
            await messageDialog.ShowAsync();

        }
    }
}
