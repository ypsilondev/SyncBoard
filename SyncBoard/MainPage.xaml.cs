﻿using Microsoft.Toolkit.Uwp.Helpers;
using Newtonsoft.Json.Linq;
using SocketIOClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.ConstrainedExecution;
using System.Threading;
using System.Timers;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Graphics.Printing;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Printing;
using Windows.UI.Xaml.Shapes;

// Die Elementvorlage "Leere Seite" wird unter https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x407 dokumentiert.

namespace SyncBoard
{
    /// <summary>
    /// Eine leere Seite, die eigenständig verwendet oder zu der innerhalb eines Rahmens navigiert werden kann.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private static int EXPAND_MARGIN = 400;

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
            
            BORDER_EXPANSION = 2000;
        private int rectangleCounter = 0;

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
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       List<InkStroke> toSync = new List<InkStroke>();

                       foreach (var stroke in args.Strokes)
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

        private async void InitSocket()
        {
            socket = new SocketIO(Network.URL, new SocketIOOptions {
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
            } catch(System.Net.WebSockets.WebSocketException e)
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
                    JObject stroke = (JObject) strokePointArray;
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

                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            InkStrokeBuilder b = new InkStrokeBuilder();

                            InkDrawingAttributes da = new InkDrawingAttributes();

                            // Pressure
                            JObject toolInfo = stroke.Value<JObject>("tool");
                            if (toolInfo !=null)
                            {
                                if (toolInfo.Value<Boolean>("pencil"))
                                {
                                    da = InkDrawingAttributes.CreateForPencil();
                                } else
                                {
                                    da.DrawAsHighlighter = toolInfo.Value<Boolean>("marker");
                                }

                                da.Size = new Size((double)toolInfo.Value<JObject>("size").GetValue("w"),
                                    (double)toolInfo.Value<JObject>("size").GetValue("h"));
                            }

                            // Color
                            da.Color = parseColor(Windows.UI.ColorHelper.FromArgb(
                                (byte)stroke.Value<JObject>("color").GetValue("A"),
                                (byte)stroke.Value<JObject>("color").GetValue("R"),
                                (byte)stroke.Value<JObject>("color").GetValue("G"),
                                (byte)stroke.Value<JObject>("color").GetValue("B")
                            ));
                            da.IgnorePressure = false;
                            da.FitToCurve = true;

                            b.SetDefaultDrawingAttributes(da);
                            InkStroke c = b.CreateStrokeFromInkPoints(inkPoints, Matrix3x2.Identity);

                            syncedStrokes.Add(Guid.Parse(stroke.Value<String>("guid")), c);
                            reverseStrokes.Add(c, Guid.Parse(stroke.Value<String>("guid")));

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
                } else if (dataJson.ContainsKey("success"))
                {
                    connectRoom();
                } else if (dataJson.ContainsKey("action"))
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
                    } else if (dataJson.Value<String>("action") == "sendBoard")
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
                    } else if (dataJson.Value<String>("action") == "left")
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
                }
                
            });

            socket.On("erase", (data) => {
                Guid[] toEraseIds = data.GetValue<Guid[]>();
                foreach (Guid eraseId in toEraseIds)
                {
                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       System.Diagnostics.Debug.WriteLine(eraseId);

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
                JArray oneStrokePoints = new JArray();
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

                foreach (var strokePoint in syncStroke.GetInkPoints())
                {
                    JObject o = new JObject();
                    o.Add("x", strokePoint.Position.X);
                    o.Add("y", strokePoint.Position.Y);
                    o.Add("p", strokePoint.Pressure);

                    oneStrokePoints.Add(o);
                }

                ö.Add("points", oneStrokePoints);

                json.Add(ö);
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

        private Color parseColor(Color color)
        {
            String theme = new Windows.UI.ViewManagement.UISettings().GetColorValue(
                Windows.UI.ViewManagement.UIColorType.Background).ToString();
            
            if (color.Equals(Colors.White) && theme == "#FFFFFFFF")
            {
                return Colors.Black;
            } else if (color.Equals(Colors.Black) && theme != "#FFFFFFFF")
            {
                return Colors.White;
            }

            return color;
        }

        private void expandBoard(bool bottom)
        {
            if (bottom)
            {
                outputGrid.Height += BORDER_EXPANSION;
                inkCanvas.Height += BORDER_EXPANSION;
            } else
            {
                outputGrid.Width += BORDER_EXPANSION;
                inkCanvas.Width += BORDER_EXPANSION;
            }

            if (inkCanvas.Height >= rectangleCounter * PRINT_RECTANGLE_HEIGHT)
            {
                for (int i = 0; i <= ((inkCanvas.Height - rectangleCounter * PRINT_RECTANGLE_HEIGHT) / BORDER_EXPANSION) + 1; i++)
                {
                    CreateNewPrintSiteBackground();
                }
            }
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
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       inkCanvas.InkPresenter.StrokeContainer.Clear();
                   });
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
            } else
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
            System.Diagnostics.Debug.WriteLine("Creating print with " + pageCount + " page(s)");

            // Clear the print-canvas
            PrintCanvas.Children.Clear();

            // Setup the required pages
            List<Panel> pagePanels = new List<Panel>();
            for (int i = 0; i < pageCount; i++)
            {
                pagePanels.Add(new ItemsStackPanel());
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
            var _printHelper = new PrintHelper(PrintCanvas);
            var printHelperOptions = new PrintHelperOptions();
            printHelperOptions.AddDisplayOption(StandardPrintTaskOptions.Orientation);
            printHelperOptions.Orientation = PrintOrientation.Portrait;

            await _printHelper.ShowPrintUIAsync("SyncBoard Print", printHelperOptions, true);
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
            for (int i = (int)Math.Max(inkCanvas.Height - BORDER_EXPANSION, 0); i <= inkCanvas.Height; i += BACKGROUND_DENSITY_DELTA)
            {
                Line line = new Line();
                line.X1 = (int)Math.Max(inkCanvas.Width - BORDER_EXPANSION, 0);
                line.X2 = inkCanvas.Width;
                line.Y1 = i;
                line.Y2 = i;

                line.Stroke = new SolidColorBrush(Color.FromArgb(50, 21, 21, 21));
                line.StrokeThickness = 1.0;
                background.Children.Add(line);
            }

            // Vertical lines
            for (int i = (int)Math.Max(inkCanvas.Width - BORDER_EXPANSION, 0); i <= inkCanvas.Width; i += BACKGROUND_DENSITY_DELTA)
            {
                Line line = new Line();
                line.X1 = i;
                line.X2 = i;
                line.Y1 = (int)Math.Max(inkCanvas.Height - BORDER_EXPANSION, 0);
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
    }
}
