using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RoDock;

static class NativeProcess
{
    [StructLayout(LayoutKind.Sequential)]
    struct ProcessBasicInformation
    {
        public nint ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(nint handle, int infoClass, ref ProcessBasicInformation info, int size, out int returned);

    /// Parent process id (kept by Windows even after the parent exits), or -1 if it can't be read.
    public static int ParentPid(Process p)
    {
        var info = new ProcessBasicInformation();
        return NtQueryInformationProcess(p.Handle, 0, ref info, Marshal.SizeOf(info), out _) == 0 ? (int)info.InheritedFromUniqueProcessId : -1;
    }
}
