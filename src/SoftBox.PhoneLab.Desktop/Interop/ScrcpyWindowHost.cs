using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SoftBox.PhoneLab.Desktop.Interop;

public sealed class ScrcpyWindowHost : HwndHost
{
    private IntPtr _parent, _child;
    public IntPtr NativeHostHandle => _parent;
    public bool HasEmbeddedWindow => _child != IntPtr.Zero && IsWindow(_child);
    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _parent = CreateWindowEx(0,"STATIC","",(int)(WsChild|WsVisible|WsClipChildren|WsClipSiblings),0,0,1,1,hwndParent.Handle,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if (_parent==IntPtr.Zero) throw new InvalidOperationException("Unable to create PhoneLab screen host.");
        return new HandleRef(this,_parent);
    }
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DetachWindow();
        if(hwnd.Handle!=IntPtr.Zero) DestroyWindow(hwnd.Handle);
        _parent=IntPtr.Zero;
    }
    public bool AttachWindow(IntPtr hwnd)
    {
        if(_parent==IntPtr.Zero||!IsWindow(hwnd)) return false;
        DetachWindow();
        var style=GetWindowLongPtr(hwnd,GwlStyle).ToInt64();
        style=(style&~(WsPopup|WsCaption|WsThickFrame|WsSysMenu|WsMinimizeBox|WsMaximizeBox|WsBorder))|WsChild|WsVisible;
        SetWindowLongPtr(hwnd,GwlStyle,new IntPtr(style));
        SetParent(hwnd,_parent);
        if(GetParent(hwnd)!=_parent) return false;
        _child=hwnd; ResizeChild();
        SetWindowPos(hwnd,IntPtr.Zero,0,0,1,1,SwpNoZOrder|SwpNoActivate|SwpFrameChanged|SwpShowWindow);
        ResizeChild(); return true;
    }
    public void DetachWindow()
    {
        var hwnd=_child; _child=IntPtr.Zero;
        if(hwnd==IntPtr.Zero||!IsWindow(hwnd)) return;
        SetParent(hwnd,IntPtr.Zero);
        var style=(GetWindowLongPtr(hwnd,GwlStyle).ToInt64()&~WsChild)|WsPopup|WsVisible;
        SetWindowLongPtr(hwnd,GwlStyle,new IntPtr(style));
        SetWindowPos(hwnd,IntPtr.Zero,0,0,1,1,SwpNoZOrder|SwpNoActivate|SwpFrameChanged);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var result=base.ArrangeOverride(size); ResizeChild(); return result;
    }
    private void ResizeChild()
    {
        if(_parent==IntPtr.Zero||!HasEmbeddedWindow||!GetClientRect(_parent,out var rc)) return;
        MoveWindow(_child,0,0,Math.Max(1,rc.Right-rc.Left),Math.Max(1,rc.Bottom-rc.Top),true);
    }
    public static IntPtr FindTopLevelWindow(int pid)
    {
        IntPtr found=IntPtr.Zero;
        EnumWindows((hwnd,_)=>{GetWindowThreadProcessId(hwnd,out var owner);if(owner!=pid||!IsWindowVisible(hwnd))return true;found=hwnd;return false;},IntPtr.Zero);
        return found;
    }
    private const int GwlStyle=-16;
    private const long WsChild=0x40000000L,WsVisible=0x10000000L,WsClipChildren=0x02000000L,WsClipSiblings=0x04000000L;
    private const long WsPopup=0x80000000L,WsCaption=0x00C00000L,WsThickFrame=0x00040000L,WsSysMenu=0x00080000L;
    private const long WsMinimizeBox=0x00020000L,WsMaximizeBox=0x00010000L,WsBorder=0x00800000L;
    private const uint SwpNoZOrder=0x0004,SwpNoActivate=0x0010,SwpFrameChanged=0x0020,SwpShowWindow=0x0040;
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",EntryPoint="CreateWindowExW",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern IntPtr CreateWindowEx(int exStyle,string cls,string name,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll",SetLastError=true)] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool MoveWindow(IntPtr hwnd,int x,int y,int w,int h,bool repaint);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll",EntryPoint="GetClientRect",SetLastError=true)] private static extern bool GetClientRect(IntPtr hwnd,out RECT rc);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool EnumWindows(EnumProc callback,IntPtr state);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int pid);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd,int index,IntPtr value);
    private delegate bool EnumProc(IntPtr hwnd,IntPtr state);
    private static IntPtr GetWindowLongPtr(IntPtr hwnd,int index)=>GetWindowLongPtr64(hwnd,index);
    private static IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value)=>SetWindowLongPtr64(hwnd,index,value);
}
