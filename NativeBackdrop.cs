using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CloudMusicFloat {
 // Accent is drawn on a non-layered HWND. Windows 11 can round this surface
 // in the compositor; the layered foreground uses the same 8 DIP radius.
 // It never activates, accepts input, or appears in the taskbar.
 sealed class NativeBackdrop : IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass{public uint Size,Style;public IntPtr Procedure;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;public string MenuName,ClassName;public IntPtr SmallIcon;}
  delegate IntPtr WindowProcedure(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
  static readonly WindowProcedure procedure=Procedure;static ushort atom;
  const string ClassName="AdoriA_Music_Rounded_Backdrop";
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern ushort RegisterClassEx(ref WindowClass value);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
  [DllImport("user32.dll")]static extern IntPtr DefWindowProc(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll")]static extern bool ValidateRect(IntPtr hwnd,IntPtr rect);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int command);
  [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr hwnd);
  [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
  public readonly IntPtr[] Handles=new IntPtr[1];public bool Enabled;
  public bool Ensure(){if(Handles[0]!=IntPtr.Zero)return true;var instance=GetModuleHandle(null);if(atom==0){var value=new WindowClass{Size=(uint)Marshal.SizeOf(typeof(WindowClass)),Procedure=Marshal.GetFunctionPointerForDelegate(procedure),Instance=instance,ClassName=ClassName};atom=RegisterClassEx(ref value);if(atom==0)return false;}Handles[0]=CreateWindowEx(0x080000a0,ClassName,"AdoriA MUSIC · 毛玻璃背景",0x88c40000,0,0,0,0,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);return Handles[0]!=IntPtr.Zero;}
  static IntPtr Procedure(IntPtr hwnd,uint message,IntPtr w,IntPtr l){if(message==0x0083)return IntPtr.Zero;if(message==0x0014)return (IntPtr)1;if(message==0x000f){ValidateRect(hwnd,IntPtr.Zero);return IntPtr.Zero;}if(message==0x0084)return (IntPtr)(-1);if(message==0x0020)return (IntPtr)1;if(message==0x0021)return (IntPtr)3;return DefWindowProc(hwnd,message,w,l);}
  public void Sync(Window window,double radius){if(Handles[0]==IntPtr.Zero)return;if(!Enabled||!window.IsVisible||window.WindowState==WindowState.Minimized){ShowWindow(Handles[0],0);return;}IntPtr main=new WindowInteropHelper(window).Handle;Rect rect;if(!GetWindowRect(main,out rect))return;int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;if(width<=0||height<=0)return;bool topmost=(GetWindowLongPtr(Handles[0],-20).ToInt64()&8)!=0;if(topmost!=window.Topmost)SetWindowPos(Handles[0],(IntPtr)(window.Topmost?-1:-2),0,0,0,0,0x0010|0x0001|0x0002|0x0200);Place(Handles[0],main,rect.Left,rect.Top,width,height);
  }
  static void Place(IntPtr handle,IntPtr main,int x,int y,int width,int height){SetWindowPos(handle,main,x,y,width,height,0x0010|0x0040|0x0200);}
  public bool ShapeReady(){int preference;return Handles[0]!=IntPtr.Zero&&DwmGetWindowAttribute(Handles[0],33,out preference,4)==0&&preference==2&&(GetWindowLongPtr(Handles[0],-20).ToInt64()&0x80000)==0;}
  public bool Aligned(Window window){Rect main,background;return Handles[0]!=IntPtr.Zero&&GetWindowRect(new WindowInteropHelper(window).Handle,out main)&&GetWindowRect(Handles[0],out background)&&main.Left==background.Left&&main.Top==background.Top&&main.Right==background.Right&&main.Bottom==background.Bottom&&((GetWindowLongPtr(Handles[0],-20).ToInt64()&8)!=0)==window.Topmost;}
  public bool Hidden{get{return Handles[0]==IntPtr.Zero||!IsWindowVisible(Handles[0]);}}
  public bool InputDisabled{get{return Handles[0]==IntPtr.Zero||(GetWindowLongPtr(Handles[0],-16).ToInt64()&0x08000000)!=0;}}
  public void Dispose(){for(int i=0;i<Handles.Length;i++)if(Handles[i]!=IntPtr.Zero){DestroyWindow(Handles[i]);Handles[i]=IntPtr.Zero;}Enabled=false;}
 }
}
