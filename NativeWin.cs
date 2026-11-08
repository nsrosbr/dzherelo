using System.Runtime.InteropServices;

namespace Dzherelo;

// Прогрес у таскбарі Windows (ITaskbarList3). Нативний зворотний зв'язок поверх ProgressBar.
// Якщо оболонки (explorer/taskbar) немає, всі виклики просто нічого не роблять — це не критичний шлях.
public sealed class TaskbarProgress
{
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
        [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig] int SetProgressState(IntPtr hwnd, int tbpFlags);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList { }

    private const int TBPF_NOPROGRESS = 0x0;
    private const int TBPF_INDETERMINATE = 0x1;
    private const int TBPF_NORMAL = 0x2;

    private ITaskbarList3? _tl;
    private IntPtr _hwnd;

    private ITaskbarList3? List()
    {
        if (_hwnd == IntPtr.Zero) return null;
        if (_tl is null)
        {
            try { _tl = (ITaskbarList3)new CTaskbarList(); _tl.HrInit(); }
            catch { _tl = null; }
        }
        return _tl;
    }

    public void Attach(IntPtr hwnd) { _hwnd = hwnd; _tl = null; }

    public void SetNoProgress() { try { List()?.SetProgressState(_hwnd, TBPF_NOPROGRESS); } catch { } }

    public void SetIndeterminate() { try { List()?.SetProgressState(_hwnd, TBPF_INDETERMINATE); } catch { } }

    public void SetValue(long done, long total)
    {
        try
        {
            var l = List();
            if (l is null) return;
            if (total <= 0) { l.SetProgressState(_hwnd, TBPF_INDETERMINATE); return; }
            l.SetProgressState(_hwnd, TBPF_NORMAL);
            l.SetProgressValue(_hwnd, (ulong)Math.Max(0, done), (ulong)total);
        }
        catch { }
    }
}
