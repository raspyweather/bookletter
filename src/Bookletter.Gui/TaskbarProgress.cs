using System.Runtime.InteropServices;

namespace Bookletter.Gui;

/// <summary>
/// Wraps the Windows shell's taskbar progress indicator (ITaskbarList3) - the same
/// green/red/yellow overlay on a taskbar button that Explorer shows during a file
/// copy. WinForms has no managed API for this (unlike WPF's TaskbarItemInfo), so it's
/// done directly via COM interop. Every call is a no-op if the shell object can't be
/// created - this is a cosmetic feature, not worth crashing the app over.
/// </summary>
internal sealed class TaskbarProgress
{
    private readonly ITaskbarList3? _taskbarList;
    private readonly IntPtr _hwnd;

    public TaskbarProgress(IntPtr windowHandle)
    {
        _hwnd = windowHandle;
        try
        {
            _taskbarList = (ITaskbarList3)new TaskbarInstance();
            _taskbarList.HrInit();
        }
        catch (COMException)
        {
            _taskbarList = null;
        }
    }

    public void SetIndeterminate() => _taskbarList?.SetProgressState(_hwnd, TBPFLAG.TBPF_INDETERMINATE);

    public void SetProgress(int completed, int total)
    {
        _taskbarList?.SetProgressState(_hwnd, TBPFLAG.TBPF_NORMAL);
        _taskbarList?.SetProgressValue(_hwnd, (ulong)Math.Max(completed, 0), (ulong)Math.Max(total, 1));
    }

    public void Clear() => _taskbarList?.SetProgressState(_hwnd, TBPFLAG.TBPF_NOPROGRESS);

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarInstance;

    // Declared in the real interface's vtable order, starting right after IUnknown -
    // COM interop dispatches by slot position, so every method up to the last one we
    // actually call (SetProgressState) has to be declared, even the ones from
    // ITaskbarList/ITaskbarList2 that this class never uses.
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3 (only the two members this class uses)
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
    }

    [Flags]
    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }
}
