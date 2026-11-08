# git credential helper: читає Windows Credential Manager (cmdkey), нічого не питає.
if ($args[0] -ne "get") { exit 0 }
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cred {
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
  [DllImport("advapi32.dll")] public static extern void CredFree(IntPtr buffer);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct CREDENTIAL {
    public uint Flags, Type; public string TargetName, Comment; public long LastWritten;
    public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist;
    public uint AttributeCount; public IntPtr Attributes; public string TargetAlias, UserName;
  }
}
"@
$ptr = [IntPtr]::Zero
if (-not [Cred]::CredRead("git:https://github.com", 1, 0, [ref]$ptr)) { exit 1 }
$c = [Runtime.InteropServices.Marshal]::PtrToStructure($ptr, [type][Cred+CREDENTIAL])
$pass = [Runtime.InteropServices.Marshal]::PtrToStringUni($c.CredentialBlob, [int]$c.CredentialBlobSize / 2)
[Cred]::CredFree($ptr)
"username=x-access-token"
"password=$pass"
