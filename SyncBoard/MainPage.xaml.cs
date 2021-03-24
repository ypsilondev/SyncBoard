using Newtonsoft.Json.Linq;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Timers;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
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

        private List<InkStroke> syncedStrokes = new List<InkStroke>();
        private SocketIO socket;
        private Boolean offlineMode = false;

        private String roomCode = "";

        public MainPage()
        {
            this.InitializeComponent();

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

            // Network and sync:
            InitSocket();

            SynchronizationTask();
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

        private async void SynchronizationTask()
        {
            System.Timers.Timer aTimer = new System.Timers.Timer(200);
            aTimer.Elapsed += timerTask;
            aTimer.AutoReset = true;
            aTimer.Enabled = true;

        }

        private void timerTask(Object source, ElapsedEventArgs e)
        {
            _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                   RunAsync(CoreDispatcherPriority.Normal, () =>
                   {
                       List<InkStroke> toSync = new List<InkStroke>();

                        foreach (var stroke in inkCanvas.InkPresenter.StrokeContainer.GetStrokes())
                        {
                            if (!syncedStrokes.Contains(stroke))
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

                               syncedStrokes.Add(stroke);
                               toSync.Add(stroke);
                            }
                        }

                       if (!offlineMode) SyncData(toSync, "sync");
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
                                        expandBoard(true, o.Value<float>("y"));
                                    }
                                    else if (o.Value<float>("x") >= inkCanvas.Width - EXPAND_MARGIN)
                                    {
                                        expandBoard(false, o.Value<float>("x"));
                                    }
                                });
                    }

                    _ = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            InkStrokeBuilder b = new InkStrokeBuilder();

                            InkDrawingAttributes da = new InkDrawingAttributes();
                            da.Color = parseColor(ColorHelper.FromArgb(
                                (byte)stroke.Value<JObject>("color").GetValue("A"),
                                (byte)stroke.Value<JObject>("color").GetValue("R"),
                                (byte)stroke.Value<JObject>("color").GetValue("G"),
                                (byte)stroke.Value<JObject>("color").GetValue("B")
                            ));
                            da.IgnorePressure = false;
                            da.FitToCurve = true;

                            b.SetDefaultDrawingAttributes(da);
                            InkStroke c = b.CreateStrokeFromInkPoints(inkPoints, Matrix3x2.Identity);

                            syncedStrokes.Add(c);

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
                                userJoinedText.Visibility = Visibility.Visible;
                                Thread.Sleep(2000);
                                userJoinedText.Visibility = Visibility.Collapsed;
                            });
                    } else if (dataJson.Value<String>("action") == "sendBoard")
                    {
                        List<InkStroke> strokes = (List<InkStroke>) inkCanvas.InkPresenter.StrokeContainer.GetStrokes();
                        SyncData(strokes, "init-sync");
                    }
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
                outputGrid.Height += 1200;
                inkCanvas.Height += 1200;
            } else
            {
                outputGrid.Width += 1200;
                inkCanvas.Width += 1200;
            }
        }

        private void expandBoard(bool bottom, float offset)
        {
            outputGrid.Height = offset + 1200;
            inkCanvas.Height = offset + 1200;

            outputGrid.Width = offset + 1200;
            inkCanvas.Width = offset + 1200;
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
    }


}
