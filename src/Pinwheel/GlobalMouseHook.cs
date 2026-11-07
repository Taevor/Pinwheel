using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Pinwheel;

public enum MouseKey
{
    None = 0,
    Middle,
    Right
}

/// <summary>
/// 用低级鼠标钩子监听全局鼠标中键、右键的按下与抬起。
/// 在独立线程上跑消息循环，保证钩子回调有消息泵可用。
/// </summary>
public sealed class GlobalMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMiddleButtonDown = 0x0207;
    private const int WmMiddleButtonUp = 0x0208;
    private const int WmRightButtonDown = 0x0204;
    private const int WmRightButtonUp = 0x0205;
    private const uint LlmhfInjected = 0x00000001;
    private const uint WmQuit = 0x0012;

    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private Thread? _thread;
    private uint _threadId;
    private bool _disposed;

    private volatile MouseKey _swallowDown = MouseKey.None;
    private volatile MouseKey _swallowUp = MouseKey.None;

    public event Action<MouseKey>? ButtonDown;
    public event Action<MouseKey>? ButtonUp;

    public MouseKey SwallowDownKey
    {
        get => _swallowDown;
        set => _swallowDown = value;
    }

    public MouseKey SwallowUpKey
    {
        get => _swallowUp;
        set => _swallowUp = value;
    }

    public GlobalMouseHook()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_thread != null)
        {
            return;
        }

        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "GlobalMouseHook"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hookId = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((info.flags & LlmhfInjected) != 0)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            int message = wParam.ToInt32();
            MouseKey key = message switch
            {
                WmMiddleButtonDown or WmMiddleButtonUp => MouseKey.Middle,
                WmRightButtonDown or WmRightButtonUp => MouseKey.Right,
                _ => MouseKey.None
            };

            if (key == MouseKey.None)
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            bool isDown = message is WmMiddleButtonDown or WmRightButtonDown;
            if (isDown)
            {
                ButtonDown?.Invoke(key);
                if (_swallowDown == key)
                {
                    _swallowDown = MouseKey.None;
                    _swallowUp = key;
                    return (IntPtr)1;
                }
            }
            else
            {
                ButtonUp?.Invoke(key);
                if (_swallowUp == key)
                {
                    _swallowUp = MouseKey.None;
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        if (_thread is { IsAlive: true })
        {
            PostThreadMessage(_threadId, WmQuit, 0, 0);
        }
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nuint wParam, nint lParam);
}
