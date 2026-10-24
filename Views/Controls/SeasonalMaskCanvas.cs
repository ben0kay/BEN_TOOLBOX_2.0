using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BEN_TOOLBOX_2_0.Models;

namespace BEN_TOOLBOX_2_0.Views.Controls;

public sealed class SeasonalMaskCanvas : Control
{
    public Action<Point>? Paint;
    public Action? StrokeEnded;
    public Action<Point>? Pick;
    public Action<Point,bool>? BranchPointPicked;
    public BranchPoint? BranchRoot { get; set; }
    public BranchPoint[] BranchTips { get; set; }=Array.Empty<BranchPoint>();
    public bool ShowBranchPoints { get; set; }
    public MaskBrush Mode { get; set; }=MaskBrush.Pan;
    public double BrushRadius { get; set; }=10;
    private Bitmap? bitmap;
    private double scale=1;
    private Vector offset;
    private Point? dragging, cursor;
    private bool painting,fit=true,space;
    public Bitmap? Source { get=>bitmap;set{bool changed=bitmap?.PixelSize!=value?.PixelSize;bitmap=value;if(fit||changed)Fit();InvalidateVisual();} }
    private static readonly IBrush Light=Brush.Parse("#28394A"),Dark=Brush.Parse("#1B2835");
    public SeasonalMaskCanvas()
    {
        ClipToBounds=true;Focusable=true;RenderOptions.SetBitmapInterpolationMode(this,BitmapInterpolationMode.None);
        LostFocus+=(_,_)=>{space=false;End();};
        SizeChanged+=(_,_)=>{if(fit)Fit();};
    }
    public void Fit()
    {
        fit=true;if(bitmap is null||Bounds.Width<=0||Bounds.Height<=0)return;
        scale=Math.Min(Bounds.Width/bitmap.PixelSize.Width,Bounds.Height/bitmap.PixelSize.Height)*0.95;
        offset=new Vector((Bounds.Width-bitmap.PixelSize.Width*scale)/2,(Bounds.Height-bitmap.PixelSize.Height*scale)/2);InvalidateVisual();
    }
    public void ActualSize()
    {
        fit=false;scale=1;
        if(bitmap is not null)offset=new Vector((Bounds.Width-bitmap.PixelSize.Width)/2,(Bounds.Height-bitmap.PixelSize.Height)/2);
        InvalidateVisual();
    }
    private Point Pixel(Point p)=>new((p.X-offset.X)/scale,(p.Y-offset.Y)/scale);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        for(int y=0;y<Bounds.Height;y+=16)for(int x=0;x<Bounds.Width;x+=16)context.FillRectangle(((x/16+y/16)&1)==0?Light:Dark,new Rect(x,y,16,16));
        if(bitmap is not null)context.DrawImage(bitmap,new Rect(bitmap.Size),new Rect(offset.X,offset.Y,bitmap.PixelSize.Width*scale,bitmap.PixelSize.Height*scale));
        if(ShowBranchPoints)
        {
            if(BranchRoot is not null)Marker(BranchRoot,Brushes.Orange);
            foreach(var tip in BranchTips)Marker(tip,Brushes.Cyan);
        }
        void Marker(BranchPoint point,IBrush colour)
        {
            var pos=new Point(offset.X+point.X*scale,offset.Y+point.Y*scale);
            context.DrawEllipse(null,new Pen(colour,2),pos,5,5);
            context.DrawLine(new Pen(colour,1),new Point(pos.X-8,pos.Y),new Point(pos.X+8,pos.Y));
        }
        if(cursor is Point point && Mode is MaskBrush.Add or MaskBrush.Remove or MaskBrush.Protect or MaskBrush.Unprotect)context.DrawEllipse(null,new Pen(Brushes.White,1),point,BrushRadius*scale,BrushRadius*scale);
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);if(bitmap is null||painting)return;
        var p=e.GetPosition(this);double next=Math.Clamp(scale*Math.Pow(1.2,e.Delta.Y),0.05,64),ratio=next/scale;
        offset=new Vector(p.X-(p.X-offset.X)*ratio,p.Y-(p.Y-offset.Y)*ratio);scale=next;fit=false;InvalidateVisual();e.Handled=true;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);Focus();if(bitmap is null)return;var point=e.GetPosition(this);var props=e.GetCurrentPoint(this).Properties;
        if(props.IsMiddleButtonPressed || (props.IsLeftButtonPressed&&(Mode==MaskBrush.Pan||space))){dragging=point;e.Pointer.Capture(this);}
        else if(props.IsLeftButtonPressed)
        {
            if(Mode is MaskBrush.BranchRoot or MaskBrush.BranchTip)BranchPointPicked?.Invoke(Pixel(point),Mode==MaskBrush.BranchRoot);
            else if(Mode==MaskBrush.PickColour)Pick?.Invoke(Pixel(point));
            else{painting=true;e.Pointer.Capture(this);Paint?.Invoke(Pixel(point));}
        }
        e.Handled=true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);var point=e.GetPosition(this);cursor=point;
        if(dragging is Point prior){offset+=point-prior;dragging=point;fit=false;}
        else if(painting)Paint?.Invoke(Pixel(point));
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {base.OnPointerReleased(e);End();e.Pointer.Capture(null);}
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);End();}
    private void End(){dragging=null;if(painting){painting=false;StrokeEnded?.Invoke();}}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.Key==Key.Space){space=true;e.Handled=true;}}
    protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);if(e.Key==Key.Space){space=false;e.Handled=true;}}
}
