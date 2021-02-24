using Newtonsoft.Json.Linq;
using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Timers;
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
        private List<InkStroke> syncedStrokes = new List<InkStroke>();
        private SocketIO socket;
        private Boolean offlineMode = false;

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
            socket = new SocketIO(Network.URL, new SocketIOOptions { EIO = 4 });
            try
            {
                await socket.ConnectAsync();
                ListenIncome();
                offlineMode = false;
                offlineModeToggleButton.IsChecked = false;
            } catch(System.Net.WebSockets.WebSocketException e)
            {
                offlineMode = true;
                offlineModeToggleButton.IsChecked = true;
            }
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

        // Call offline mode
        private void offlineModeToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            this.offlineMode = (bool) offlineModeToggleButton.IsChecked;

            if (this.offlineMode)
            {
                InitSocket();
            }
        }
    }


}
