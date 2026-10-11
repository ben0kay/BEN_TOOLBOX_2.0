using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace BEN_TOOLBOX_2_0.Services;

// RegisterHotKey requires a thread with a Windows message loop. The thread and hotkey
// live for as long as the toolbox application; closing it releases the hotkey.
public sealed class ScreenshotHotkey : IDisposable
{
    private const uint Ctrl=0x0002,Shift=0x0004,NoRepeat=0x4000,VkV=0x56,Quit=0x0012,Hotkey=0x0312;
    private const int Id=0xA106;
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
        public uint Private;
    }
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg,IntPtr hwnd,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id,uint msg,UIntPtr wparam,IntPtr lparam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Msg msg,IntPtr hwnd,uint min,uint max,uint remove);
    private readonly Thread thread;
    private readonly Action<string> feedback;
    private readonly ManualResetEventSlim ready=new(false);
    private uint threadId;
    private bool disposed;
    public bool Registered {get;private set;}
    public ScreenshotHotkey(Action<string> feedback)
    {
        this.feedback=feedback;
        thread=new Thread(Loop){IsBackground=true,Name="BEN Toolbox screenshot hotkey"};
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(3));
    }
    private void Loop()
    {
        threadId=GetCurrentThreadId();
        PeekMessage(out _,IntPtr.Zero,0,0,0); // establish message queue for PostThreadMessage
        Registered=RegisterHotKey(IntPtr.Zero,Id,Ctrl|Shift|NoRepeat,VkV);
        ready.Set();
        if(!Registered)return;
        try
        {
            while(GetMessage(out var message,IntPtr.Zero,0,0)>0)
            {
                if(message.Message==Hotkey&&message.WParam==(UIntPtr)Id)
                {
                    try
                    {
                        var path=ClipboardPngService.CaptureAndSave();
                        Dispatcher.UIThread.Post(()=>feedback("Screenshot saved: "+path));
                    }
                    catch(Exception ex){Dispatcher.UIThread.Post(()=>feedback("Screenshot failed: "+ex.Message));}
                }
                else {TranslateMessage(ref message);DispatchMessage(ref message);}
            }
        }
        finally{UnregisterHotKey(IntPtr.Zero,Id);}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(threadId!=0&&Registered)PostThreadMessage(threadId,Quit,UIntPtr.Zero,IntPtr.Zero);
        ready.Dispose();
    }
}
