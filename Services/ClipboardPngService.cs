using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using SixLabors.ImageSharp;

namespace BEN_TOOLBOX_2_0.Services;

// Windows clipboard CF_DIB/PNG reader; no dependency on WinForms or a Windows-specific target framework.
public static class ClipboardPngService
{
    private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BEN_TOOLBOX_2_0", "ClipboardImages.json");
    private sealed class Settings { public string? Folder { get; set; } }
    public static string Destination
    {
        get
        {
            try { var saved=JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile))?.Folder;
                if(!string.IsNullOrWhiteSpace(saved))return saved; }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){}
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),"Screenshots","BEN Toolbox");
        }
        set
        {
            if(string.IsNullOrWhiteSpace(value)||!Directory.Exists(value))throw new DirectoryNotFoundException("Select an existing destination folder.");
            var directory=Path.GetDirectoryName(SettingsFile)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsFile,JsonSerializer.Serialize(new Settings{Folder=Path.GetFullPath(value)}));
        }
    }

    private const uint CfDib=8;
    private const uint CfDibV5=17;
    [DllImport("user32.dll",SetLastError=true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern nuint GlobalSize(IntPtr handle);

    private static byte[]? CopyClipboardBlock(uint format)
    {
        var handle=GetClipboardData(format);if(handle==IntPtr.Zero)return null;
        var pointer=GlobalLock(handle);if(pointer==IntPtr.Zero)return null;
        try
        {
            nuint length=GlobalSize(handle);
            if(length==0||length>128*1024*1024)return null;
            var bytes=new byte[(int)length];Marshal.Copy(pointer,bytes,0,bytes.Length);return bytes;
        }
        finally{GlobalUnlock(handle);}
    }
    public static byte[] ReadPng()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows clipboard required.");
        bool opened=false;
        for(int i=0;i<8;i++)
        {
            if(OpenClipboard(IntPtr.Zero)){opened=true;break;}
            Thread.Sleep(30);
        }
        if(!opened)throw new IOException("Clipboard is busy. Try again.");
        try
        {
            var png=CopyClipboardBlock(RegisterClipboardFormat("PNG"));
            if(png!=null&&png.Length>8&&png[0]==137&&png[1]==80)
                return png;
            var dib=CopyClipboardBlock(CfDib)??CopyClipboardBlock(CfDibV5);
            if(dib==null||dib.Length<40)throw new IOException("Clipboard contains no supported image. Use Win + Shift + S first.");
            // A DIB needs a BITMAPFILEHEADER to decode as BMP. Derive the pixel offset from
            // the header and colour table, including optional BI_BITFIELDS masks.
            int headerSize=BitConverter.ToInt32(dib,0);
            if(headerSize<40||headerSize>dib.Length)throw new IOException("Unsupported clipboard bitmap.");
            int bits=BitConverter.ToUInt16(dib,14),compression=BitConverter.ToInt32(dib,16);
            uint colors=BitConverter.ToUInt32(dib,32);
            long palette=(long)(colors!=0?colors:(bits<=8?1<<bits:0))*4;
            long masks=headerSize==40&&(compression==3||compression==6)?(compression==6?16:12):0;
            long offset=14L+headerSize+masks+palette;
            if(offset<54||offset>dib.Length+14L||dib.Length>int.MaxValue-14)
                throw new IOException("Unsupported clipboard image format.");
            byte[] bmp=new byte[dib.Length+14];
            bmp[0]=(byte)'B';bmp[1]=(byte)'M';
            BitConverter.GetBytes(bmp.Length).CopyTo(bmp,2);
            BitConverter.GetBytes((int)offset).CopyTo(bmp,10);
            Buffer.BlockCopy(dib,0,bmp,14,dib.Length);
            using var image=SixLabors.ImageSharp.Image.Load(bmp);
            using var stream=new MemoryStream();
            image.SaveAsPng(stream);
            return stream.ToArray();
        }
        finally{CloseClipboard();}
    }
    public static string Save(byte[] png)
    {
        var folder=Destination;Directory.CreateDirectory(folder);
        var date=DateTime.Now.ToString("yyyyMMdd");
        for(int n=1;n<=99999;n++)
        {
            var path=Path.Combine(folder,$"Screenshot_{date}_{n:D4}.png");
            try{using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                stream.Write(png);return path;}
            catch(IOException) when(File.Exists(path)){continue;}
        }
        throw new IOException("Too many screenshots for the current date.");
    }
    public static string CaptureAndSave()=>Save(ReadPng());
}
