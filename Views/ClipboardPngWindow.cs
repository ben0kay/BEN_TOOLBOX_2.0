using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using BEN_TOOLBOX_2_0.Services;

namespace BEN_TOOLBOX_2_0.Views;

public sealed class ClipboardPngWindow : Window
{
    private readonly TextBox folder=new(){Watermark="Destination folder"};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Image preview=new(){MaxWidth=600,MaxHeight=340,Stretch=Stretch.Uniform};
    private byte[]? current;
    private Bitmap? bitmap;
    public ClipboardPngWindow()
    {
        Title="Clipboard to PNG / BEN Toolbox";Width=850;Height=720;MinWidth=600;MinHeight=460;
        Background=Brush.Parse("#091522");Foreground=Brush.Parse("#E8F4FF");
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(22),Spacing=14};
        panel.Children.Add(new TextBlock{Text="CLIPBOARD TO PNG",FontSize=24,FontWeight=FontWeight.Bold,Foreground=Brush.Parse("#86E5F6")});
        panel.Children.Add(new TextBlock{Text="Take a snip with Win + Shift + S. Press Ctrl + Shift + V anywhere while BEN Toolbox is running for quick-save, or use Preview and Save here.",TextWrapping=TextWrapping.Wrap});
        folder.Text=ClipboardPngService.Destination;
        var chooser=new Grid{ColumnDefinitions=new ColumnDefinitions("*,115"),ColumnSpacing=10};
        chooser.Children.Add(folder);
        var browse=new Button{Content="BROWSE"};
        browse.Click+=async (_,_)=>{
            var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{AllowMultiple=false});
            if(folders.Count>0&&folders[0].Path.IsFile)folder.Text=folders[0].Path.LocalPath;
        };
        Grid.SetColumn(browse,1);chooser.Children.Add(browse);panel.Children.Add(chooser);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};
        var setFolder=new Button{Content="SET QUICK-SAVE FOLDER"};
        var load=new Button{Content="PREVIEW CLIPBOARD"};
        var save=new Button{Content="SAVE PNG"};
        actions.Children.Add(setFolder);actions.Children.Add(load);actions.Children.Add(save);panel.Children.Add(actions);
        panel.Children.Add(status);panel.Children.Add(new Border{Background=Brush.Parse("#142538"),Padding=new Thickness(12),CornerRadius=new CornerRadius(10),Child=preview,MinHeight=340});
        Content=new ScrollViewer{Content=panel};
        setFolder.Click+=(_,_)=>SetFolder();
        load.Click+=(_,_)=>Load();
        save.Click+=(_,_)=>Save();
        Closed+=(_,_)=>{bitmap?.Dispose();};
        status.Text="Ready. Quick-save folder: "+folder.Text;
    }
    private bool SetFolder()
    {
        try{ClipboardPngService.Destination=(folder.Text??"").Trim().Trim('"');status.Text="Quick-save folder set.";return true;}
        catch(Exception ex){status.Text=ex.Message;return false;}
    }
    private void Load()
    {
        try
        {
            current=ClipboardPngService.ReadPng();
            var next=new Bitmap(new MemoryStream(current));
            preview.Source=next;bitmap?.Dispose();bitmap=next;
            status.Text="Screenshot loaded. Click Save PNG.";
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
    private void Save()
    {
        if(!SetFolder())return;
        try
        {
            current??=ClipboardPngService.ReadPng();
            var path=ClipboardPngService.Save(current);
            status.Text="Saved: "+path;
            current=null;
        }
        catch(Exception ex){status.Text=ex.Message;}
    }
}
