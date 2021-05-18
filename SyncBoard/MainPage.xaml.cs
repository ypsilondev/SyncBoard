using Newtonsoft.Json.Linq;
using SocketIOClient;
using SyncBoard.UserControls;
using SyncBoard.Utiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Data.Pdf;
using Windows.Foundation;
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
using Windows.UI.Xaml.Shapes;

namespace SyncBoard
{
    public sealed partial class MainPage : Page
    {
        private static int EXPAND_MARGIN = 600;


        public Dictionary<Guid, InkStroke> syncedStrokes = new Dictionary<Guid, InkStroke>();
        public Dictionary<InkStroke, Guid> reverseStrokes = new Dictionary<InkStroke, Guid>();

        private SocketIO socket;
        private Boolean offlineMode = false;

        private String roomCode = "";

        public static int PAGE_HEIGHT = 1123, PAGE_WIDTH = 794,

            PRINT_RECTANGLE_WIDTH = 794,
            PRINT_RECTANGLE_HEIGHT = 1123,
            AMOUNT_INITIAL_RECTANGLES = 2,

            BORDER_EXPANSION = 1128;

        public static double PAGE_SITE_RATIO = (double)PRINT_RECTANGLE_HEIGHT / PRINT_RECTANGLE_WIDTH;

        public uint rectangleCounter = 0, pdfSiteCounter = 0;
        internal static MainPage Instance;

        public MainPage()
        {
            this.InitializeComponent();
            MainPage.Instance = this;

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
            DrawStrokesFromList(args.Strokes.ToList());
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
                    InkStroke c = StrokeUtil.ParseFromJSON(stroke);

                    syncedStrokes.Add(Guid.Parse(stroke.Value<String>("guid")), c);
                    reverseStrokes.Add(c, Guid.Parse(stroke.Value<String>("guid")));

                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                        RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            inkCanvas.InkPresenter.StrokeContainer.AddStroke(c);
                        });
                }

                // TODO FIXME: this is the code for the new protocol-version of syncboard
                /*
                JObject updateStrokes = data.GetValue<JObject>();
                
                foreach(var strokeArray in updateStrokes)
                {
                    JObject stroke = (JObject)strokeArray.Value;
                    InkStroke c = StrokeUtil.ParseFromJSON(stroke);

                    syncedStrokes.Add(Guid.Parse(strokeArray.Key), c);
                    reverseStrokes.Add(c, Guid.Parse(strokeArray.Key));

                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                        RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            inkCanvas.InkPresenter.StrokeContainer.AddStroke(c);
                        });
                }*/
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
            //JObject json = new JObject();

            toSync.ForEach(syncStroke =>
            {
                //json.Add(reverseStrokes.GetValueOrDefault(syncStroke).ToString(), StrokeUtil.CreateJSONStrokeFrom(syncStroke));
                json.Add(StrokeUtil.CreateJSONStrokeFrom(syncStroke));
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

        public void expandBoard(bool bottom)
        {
            if (bottom)
            {
                expandBoard(0, BORDER_EXPANSION);
            }
            else
            {
                expandBoard(BORDER_EXPANSION, 0);
            }
        }

        public void expandBoard(int x, int y)
        {
            outputGrid.Height += y;
            inkCanvas.Height += y;

            outputGrid.Width += x;
            inkCanvas.Width += x;

            while (inkCanvas.Height >= rectangleCounter * PRINT_RECTANGLE_HEIGHT)
            {
                CreateNewPrintSiteBackground();
            }

            CreateBackground();
        }

        public void expandBoard(float offset)
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
                this.ClearRoom();
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
            this.ToggleFullscreen();
        }

        public void ToggleFullscreen()
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

        

        

        // Print PDF
        public async void Printer_Click(object sender, RoutedEventArgs e)
        {
            PrintUtil.PrintCanvas(inkCanvas, imports, PrintCanvas);
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


        public void CreateBackground()
        {
            CreateBackground(false);
        }

        // Background lines
        public void CreateBackground(Boolean inital)
        {
            // Horizontal lines
            int start = (int)Math.Max(inkCanvas.Height - BORDER_EXPANSION, 0);
            if (inkCanvas.Height - BORDER_EXPANSION < BORDER_EXPANSION || inital)
            {
                start = 0;
                background.Children.Clear();
            }
            for (int i = start; i <= inkCanvas.Height; i += SettingsPage.BACKGROUND_DENSITY_DELTA)
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
            for (int i = 0; i <= Window.Current.Bounds.Width; i += SettingsPage.BACKGROUND_DENSITY_DELTA)
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

                new PdfImport(doc, imports, this).Load();

            }
            catch (Exception ignored)
            {

            }
        }

        // Create JSON export
        private async void CreateJSONExport(object sender, RoutedEventArgs e)
        {
            JObject board = new JObject();

            foreach (Guid key in syncedStrokes.Keys)
            {
                board.Add(key.ToString(), StrokeUtil.CreateJSONStrokeFrom(syncedStrokes.GetValueOrDefault(key)));
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
                InkStroke c = StrokeUtil.ParseFromJSON(obj);

                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        inkCanvas.InkPresenter.StrokeContainer.AddStroke(c);
                    });

                syncingStrokes.Add(c);
            }

            // TODO FIXME: this is the implementation for the new protocol
            /*List<InkStroke> syncingStrokes = new List<InkStroke>();
            foreach (JObject obj in board)
            {
                InkStroke c = StrokeUtil.ParseFromJSON(obj);

                _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
                    {
                        inkCanvas.InkPresenter.StrokeContainer.AddStroke(c);
                    });

                syncingStrokes.Add(c);
            }*/

            DrawStrokesFromList(syncingStrokes);
        }

        public void TestForBoardExpansion(float x, float y)
        {
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                        RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            if (y >= inkCanvas.Height - EXPAND_MARGIN)
                            {
                                expandBoard(y);
                            }
                            else if (x >= inkCanvas.Width - EXPAND_MARGIN)
                            {
                                expandBoard(x);
                            }
                        });
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

        private void DrawStrokesFromList(List<InkStroke> strokes)
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

        public async void DisplayMessage(String msg)
        {
            var messageDialog = new MessageDialog(msg);
            messageDialog.Commands.Add(new UICommand("Close"));

            // Set the command that will be invoked by default
            messageDialog.DefaultCommandIndex = 0;

            // Set the command to be invoked when escape is pressed
            messageDialog.CancelCommandIndex = 1;

            // Show the message dialog
            await messageDialog.ShowAsync();

        }

        public UserControl GetSettingsPage()
        {
            return settingsPage;
        }

    }
}
