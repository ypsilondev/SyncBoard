using Newtonsoft.Json.Linq;
using SimpleJson;
using SocketIOClient;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using WebSocket4Net;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Input;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;
using JsonArray = SimpleJson.JsonArray;
using JsonObject = SimpleJson.JsonObject;

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
            await socket.ConnectAsync();
            ListenIncome();
        }

        private async void SynchronizationTask()
        {
            Timer timer = new Timer((e) =>
            {
                Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
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

                        SyncData(toSync);
                    });
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }

        private async void ListenIncome()
        {
            socket.On("sync", (data) =>
            {
                System.Diagnostics.Debug.WriteLine(data);
                JArray updateStrokes = data.GetValue<JArray>();
                

                foreach (var strokePointArray in updateStrokes)
                {
                    JObject stroke = (JObject) strokePointArray;

                    InkStrokeBuilder b = new InkStrokeBuilder();
                    List<InkPoint> inkPoints = new List<InkPoint>();
                    /*Matrix3x2 matrix = new Matrix3x2(
                        stroke.Value<float>("M11"),
                        stroke.Value<float>("M12"),
                        stroke.Value<float>("M21"),
                        stroke.Value<float>("M22"),
                        stroke.Value<float>("M31"),
                        stroke.Value<float>("M32")
                    );*/
                    Matrix3x2 matrix = Matrix3x2.Identity;

                    foreach (var point in stroke.Value<JArray>("points"))
                    {
                        JObject o = (JObject)point;
                        Point p = new Point(o.Value<float>("X"), o.Value<float>("Y"));

                        InkPoint ip = new InkPoint(p, o.Value<float>("p"));
                        inkPoints.Add(ip);
                    }

                    InkStroke toAddStroke = b.CreateStrokeFromInkPoints(inkPoints, matrix);

                    Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.
                    RunAsync(CoreDispatcherPriority.Low, () =>
                        {
                            if (!inkCanvas.InkPresenter.StrokeContainer.GetStrokes().Contains(toAddStroke))
                            {
                                System.Diagnostics.Debug.WriteLine("fdgfsh<");
                                inkCanvas.InkPresenter.StrokeContainer.AddStroke(toAddStroke);
                            }

                            syncedStrokes.Add(toAddStroke);
                        });
                }
            });
        }

        private void SyncData(List<InkStroke> toSync)
        {
            Newtonsoft.Json.Linq.JArray json = new Newtonsoft.Json.Linq.JArray();

            toSync.ForEach(syncStroke =>
            {
                Newtonsoft.Json.Linq.JArray oneStrokePoints = new Newtonsoft.Json.Linq.JArray();
                Newtonsoft.Json.Linq.JObject matrix = new Newtonsoft.Json.Linq.JObject();
                Newtonsoft.Json.Linq.JObject ö = new Newtonsoft.Json.Linq.JObject();

                matrix.Add("M11", syncStroke.PointTransform.M11);
                matrix.Add("M12", syncStroke.PointTransform.M12);
                matrix.Add("M21", syncStroke.PointTransform.M21);
                matrix.Add("M22", syncStroke.PointTransform.M22);
                matrix.Add("M31", syncStroke.PointTransform.M31);
                matrix.Add("M32", syncStroke.PointTransform.M32);

                foreach (var strokePoint in syncStroke.GetInkPoints())
                {
                    Newtonsoft.Json.Linq.JObject o = new Newtonsoft.Json.Linq.JObject();
                    o.Add("x", strokePoint.Position.X);
                    o.Add("y", strokePoint.Position.Y);
                    o.Add("p", strokePoint.Pressure);

                    oneStrokePoints.Add(o);
                }

                ö.Add("matrix", matrix);
                ö.Add("points", oneStrokePoints);

                json.Add(ö);
            });

            socket.EmitAsync("sync", json);
        }

        // Standard color
        private void ComboBoxItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            InkDrawingAttributes drawingAttributes = new InkDrawingAttributes();
            String theme = new Windows.UI.ViewManagement.UISettings().GetColorValue(
                Windows.UI.ViewManagement.UIColorType.Background).ToString();
            drawingAttributes.Color = theme == "#FFFFFFFF" ? Windows.UI.Colors.Black : Windows.UI.Colors.White;
            inkCanvas.InkPresenter.UpdateDefaultDrawingAttributes(drawingAttributes);
        }

        // Red color
        private void ComboBoxItem_Tapped_1(object sender, TappedRoutedEventArgs e)
        {

        }
    }


}
