using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
namespace BEN_TOOLBOX_2_0.Features.Inspectors.InternetObservatory;
public sealed class WorldMapView:Control
{
    private static readonly Lazy<StreamGeometry> Land=new(LoadLand);
    private static readonly IBrush Ocean=Brush.Parse("#061724"),LandFill=Brush.Parse("#103445");
    private static readonly Pen GridPen=new(Brush.Parse("#143444"),1);
    private Earthquake[] events=Array.Empty<Earthquake>();
    public WorldProjection Projection{get;}=new();
    public string? SelectedId{get;set;}
    public event Action<Earthquake>? EventSelected;
    public event Action? ViewChanged;
    private Point? start,last;private bool dragged;
    public WorldMapView(){Focusable=true;ClipToBounds=true;MinHeight=300;}
    public void SetEvents(Earthquake[] value){events=value;InvalidateVisual();}
    public void Fit(){Projection.Set(1,0,0);Changed();}
    private void Changed(){InvalidateVisual();ViewChanged?.Invoke();}
    protected override void OnSizeChanged(SizeChangedEventArgs e){base.OnSizeChanged(e);Projection.Resize(Bounds.Width,Bounds.Height);Changed();}
    public Point Project(double longitude,double latitude){var p=Projection.Project(longitude,latitude);return new(p.X,p.Y);}
    public static double Radius(Earthquake e)=>e.Magnitude is {} m?Math.Clamp(2+m*.8,3,12):4;
    private static IBrush Colour(Earthquake e)=>e.DepthKm is null?Brushes.LightGray:e.DepthKm<70?Brush.Parse("#41DEED"):e.DepthKm<300?Brush.Parse("#F4BB62"):Brush.Parse("#CB94F4");
    public override void Render(DrawingContext c)
    {
        base.Render(c);c.FillRectangle(Ocean,new Rect(Bounds.Size));using(c.PushClip(new Rect(Bounds.Size)))
        {
            for(int lon=-180;lon<=180;lon+=30)c.DrawLine(GridPen,Project(lon,-90),Project(lon,90));
            for(int lat=-90;lat<=90;lat+=30)c.DrawLine(GridPen,Project(-180,lat),Project(180,lat));
            var origin=Project(0,0);double scale=Projection.Scale;
            using(c.PushTransform(new Matrix(scale,0,0,scale,origin.X,origin.Y)))c.DrawGeometry(LandFill,new Pen(Brush.Parse("#288AA1"),1/scale),Land.Value);
            foreach(var e in events)
            {
                var p=Project(e.Longitude,e.Latitude);double r=Radius(e);if(p.X< -r||p.X>Bounds.Width+r||p.Y< -r||p.Y>Bounds.Height+r)continue;
                c.DrawEllipse(Colour(e),new Pen(Ocean,1),p,r,r);
                if(e.Id==SelectedId){c.DrawEllipse(null,new Pen(Brushes.White,2),p,r+4,r+4);c.DrawEllipse(null,new Pen(Brush.Parse("#47DCFF"),1),p,r+7,r+7);}
            }
        }
    }
    private static double Distance(Point a,Point b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    public Earthquake? HitTestEvent(Point point)=>events.Select(e=>(Event:e,Distance:Distance(Project(e.Longitude,e.Latitude),point))).Where(x=>x.Distance<=Radius(x.Event)+5).OrderBy(x=>x.Distance).Select(x=>x.Event).FirstOrDefault();
    protected override void OnPointerPressed(PointerPressedEventArgs e){base.OnPointerPressed(e);if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;Focus();start=last=e.GetPosition(this);dragged=false;e.Pointer.Capture(this);e.Handled=true;}
    protected override void OnPointerMoved(PointerEventArgs e){base.OnPointerMoved(e);if(last is not {} before)return;var now=e.GetPosition(this);if(start is {} began&&Distance(now,began)>4)dragged=true;if(dragged){Projection.Pan(now.X-before.X,now.Y-before.Y);Changed();}last=now;}
    protected override void OnPointerReleased(PointerReleasedEventArgs e){base.OnPointerReleased(e);if(last is null)return;if(!dragged&&HitTestEvent(e.GetPosition(this)) is {} selected){SelectedId=selected.Id;EventSelected?.Invoke(selected);InvalidateVisual();}start=last=null;e.Pointer.Capture(null);e.Handled=true;}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);start=last=null;}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e){base.OnPointerWheelChanged(e);var p=e.GetPosition(this);Projection.ZoomAt(Math.Pow(1.2,Math.Clamp(e.Delta.Y,-4,4)),p.X,p.Y);Changed();e.Handled=true;}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);switch(e.Key)
        {
            case Key.Home:Projection.Set(1,0,0);break;
            case Key.Left:Projection.Pan(40,0);break;case Key.Right:Projection.Pan(-40,0);break;case Key.Up:Projection.Pan(0,40);break;case Key.Down:Projection.Pan(0,-40);break;
            case Key.Add:case Key.OemPlus:Projection.ZoomAt(1.3,Bounds.Width/2,Bounds.Height/2);break;
            case Key.Subtract:case Key.OemMinus:Projection.ZoomAt(1/1.3,Bounds.Width/2,Bounds.Height/2);break;default:return;
        }Changed();e.Handled=true;
    }
    private static StreamGeometry LoadLand()
    {
        using var stream=AssetLoader.Open(new Uri("avares://BEN_TOOLBOX_2_0/Assets/InternetObservatory/world_110m.json"));var rings=JsonSerializer.Deserialize<double[][][]>(stream)??throw new InvalidDataException("Bundled geography missing.");var geometry=new StreamGeometry();using var draw=geometry.Open();
        foreach(var ring in rings){if(ring.Length<3)continue;draw.BeginFigure(new Point(ring[0][0],-ring[0][1]),true);for(int i=1;i<ring.Length;i++)draw.LineTo(new Point(ring[i][0],-ring[i][1]));draw.EndFigure(true);}return geometry;
    }
}
