using Newtonsoft.Json.Linq;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Timers;
using Windows.Data.Json;
using Windows.Foundation;
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
        private static int EXPAND_MARGIN = 100;

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
            String theme = new Windows.UI.ViewManagement.UISettings().GetColorValue(
                Windows.UI.ViewManagement.UIColorType.Background).ToString();
            drawingAttributes.Color = theme == "#FFFFFFFF" ? Windows.UI.Colors.Black : Windows.UI.Colors.White;
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
                                syncedStrokes.Add(stroke);
                                toSync.Add(stroke);
                            }
                        }

                       if (!offlineMode) SyncData(toSync);
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

                            /*
                            Polygon polygon = new Polygon();
                            inkPoints.ForEach(p =>
                            {
                                polygon.Points.Add(p);
                            });

                            var brush = new SolidColorBrush(Windows.UI.ColorHelper.FromArgb(
                                (byte) stroke.Value<JObject>("color").GetValue("A"),
                                (byte) stroke.Value<JObject>("color").GetValue("R"),
                                (byte) stroke.Value<JObject>("color").GetValue("G"),
                                (byte) stroke.Value<JObject>("color").GetValue("B")
                                ));
                            polygon.Stroke = brush;

                            selectionCanvas.Children.Add(polygon);*/
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

                }
                
            });
        }

        private void SyncData(List<InkStroke> toSync)
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

                    if (strokePoint.Position.Y >= inkCanvas.Height - EXPAND_MARGIN)
                    {
                        expandBoard(true);
                    } else if (strokePoint.Position.X >= inkCanvas.Width - EXPAND_MARGIN)
                    {
                        expandBoard(false);
                    }

                    oneStrokePoints.Add(o);
                }

                ö.Add("points", oneStrokePoints);

                json.Add(ö);
            });

            socket.EmitAsync("sync", json);
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
                outputGrid.Height += 200;
                inkCanvas.Height += 200;
            } else
            {
                outputGrid.Width += 200;
                inkCanvas.Width += 200;
            }
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
    }


}
