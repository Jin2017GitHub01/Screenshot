using System.Runtime.InteropServices;
using static Screenshot.NativeMethods;

namespace Screenshot;

/// <summary>
/// Installs a low-level keyboard hook (<see cref="NativeMethods.WH_KEYBOARD_LL"/>) that
/// intercepts the <c>Print Screen</c> (<see cref="NativeMethods.VK_SNAPSHOT"/>) key and hardware
/// Snipping keys (<c>Win+Shift+S</c> / <c>Win+Shift+F23</c>) when enabled.
/// When disabled or disposed, the hook is removed and Windows Snipping Tool resumes default behavior.
/// </summary>
internal sealed class PrintScreenHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _snapshotKeyDown;
    private bool _snipChordKeyDown;
    private bool _winDown;
    private bool _shiftDown;

    public event EventHandler? PrintScreenPressed;

    public PrintScreenHook()
    {
        // Keep the delegate rooted in an instance field so the GC never collects it while hooked.
        _proc = HookCallback;
    }

    public bool IsEnabled => _hookId != IntPtr.Zero;

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (_hookId != IntPtr.Zero) return;
            IntPtr hMod = GetModuleHandle(null);
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
            if (_hookId == IntPtr.Zero)
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        }
        else
        {
            if (_hookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _snapshotKeyDown = false;
            _snipChordKeyDown = false;
            _winDown = false;
            _shiftDown = false;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            bool isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    _winDown = isKeyDown;
                else if (vkCode == VK_SHIFT || vkCode == VK_LSHIFT || vkCode == VK_RSHIFT)
                    _shiftDown = isKeyDown;

                // 1. Standard Print Screen key (VK_SNAPSHOT).
                // Must swallow BOTH key-down and key-up: Windows 11 Snipping Tool triggers on
                // VK_SNAPSHOT WM_KEYUP, and some keyboard drivers only emit WM_KEYUP for PrtScn.
                if (vkCode == VK_SNAPSHOT)
                {
                    if (isKeyDown)
                    {
                        if (!_snapshotKeyDown)
                        {
                            _snapshotKeyDown = true;
                            PrintScreenPressed?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    else if (isKeyUp)
                    {
                        if (_snapshotKeyDown)
                            _snapshotKeyDown = false;
                        else
                            PrintScreenPressed?.Invoke(this, EventArgs.Empty);
                    }
                    return (IntPtr)1;
                }

                // 2. Hardware Snipping key on modern Windows 11 laptops/keyboards, which emits
                // Win+Shift+S (0x53) or Win+Shift+F23 (0x86) instead of VK_SNAPSHOT.
                if (vkCode == VK_S || vkCode == VK_F23)
                {
                    bool win = _winDown || IsKeyPressed(VK_LWIN) || IsKeyPressed(VK_RWIN);
                    bool shift = _shiftDown || IsKeyPressed(VK_SHIFT) || IsKeyPressed(VK_LSHIFT) || IsKeyPressed(VK_RSHIFT);
                    bool ctrl = IsKeyPressed(VK_CONTROL);
                    bool alt = IsKeyPressed(VK_MENU);

                    if (isKeyDown && win && shift && !ctrl && !alt)
                    {
                        if (!_snipChordKeyDown)
                        {
                            _snipChordKeyDown = true;
                            PrintScreenPressed?.Invoke(this, EventArgs.Empty);
                        }
                        return (IntPtr)1;
                    }
                    if (isKeyUp && _snipChordKeyDown)
                    {
                        _snipChordKeyDown = false;
                        return (IntPtr)1;
                    }
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsKeyPressed(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    public void Dispose() => SetEnabled(false);
}
