using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.UI.Input.Inking;

namespace SyncBoard.Utiles
{
    class StrokeUtil
    {

        public static InkStroke ParseFromJSON(JObject stroke)
        {
            List<InkPoint> inkPoints = new List<InkPoint>();

            foreach (var point in stroke.Value<JArray>("points"))
            {
                JObject o = (JObject)point;
                Point p = new Point(o.Value<float>("x"), o.Value<float>("y"));
                InkPoint ip = new InkPoint(p, o.Value<float>("p"));
                inkPoints.Add(ip);

                MainPage.Instance.TestForBoardExpansion(o.Value<float>("x"), o.Value<float>("y"));
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

        public static JObject CreateJSONStrokeFrom(InkStroke syncStroke)
        {
            JObject ö = new JObject();

            JObject color = new JObject();
            color.Add("A", syncStroke.DrawingAttributes.Color.A);
            color.Add("R", syncStroke.DrawingAttributes.Color.R);
            color.Add("G", syncStroke.DrawingAttributes.Color.G);
            color.Add("B", syncStroke.DrawingAttributes.Color.B);

            ö.Add("color", color);
            ö.Add("guid", MainPage.Instance.reverseStrokes.GetValueOrDefault(syncStroke));

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


    }
}
