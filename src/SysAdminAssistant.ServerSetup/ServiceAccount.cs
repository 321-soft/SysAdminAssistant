#nullable disable
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
namespace SysAdminInstaller {
 public static class ServiceAccount {
  [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Length; public IntPtr Root, Name; public uint Flags; public IntPtr Descriptor, Quality; }
  [StructLayout(LayoutKind.Sequential)] struct LsaString { public ushort Length, MaximumLength; public IntPtr Buffer; }
  [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system, ref Attributes attributes, uint access, out IntPtr policy);
  [DllImport("advapi32.dll")] static extern uint LsaEnumerateAccountRights(IntPtr policy, byte[] sid, out IntPtr rights, out uint count);
  [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LsaString[] rights, uint count);
  [DllImport("advapi32.dll")] static extern uint LsaRemoveAccountRights(IntPtr policy, byte[] sid, [MarshalAs(UnmanagedType.Bool)] bool all, LsaString[] rights, uint count);
  [DllImport("advapi32.dll")] static extern uint LsaNtStatusToWinError(uint status);
  [DllImport("advapi32.dll")] static extern uint LsaFreeMemory(IntPtr memory);
  [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr policy);
  static void Check(uint status) { if (status != 0) throw new Win32Exception((int)LsaNtStatusToWinError(status)); }
  public static void ServiceRights(string sid) {
   Change(sid,false,"SeServiceLogonRight");
   Change(sid,false,"SeDenyInteractiveLogonRight");
   Change(sid,false,"SeDenyRemoteInteractiveLogonRight");
  }
  public static void RemoveManagedRights(string sid) {
   foreach(var right in new[]{"SeServiceLogonRight","SeDenyInteractiveLogonRight","SeDenyRemoteInteractiveLogonRight"}) Change(sid,true,right);
  }
  static bool Change(string sidValue, bool remove, string rightName) {
   var sid=new SecurityIdentifier(sidValue); var bytes=new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes,0);
   var attributes=new Attributes(); attributes.Length=Marshal.SizeOf(typeof(Attributes)); IntPtr policy;
   Check(LsaOpenPolicy(IntPtr.Zero,ref attributes,0x810,out policy));
   IntPtr buffer=IntPtr.Zero;
   try {
    uint count; uint status=LsaEnumerateAccountRights(policy,bytes,out buffer,out count);
    bool present=false;
    if(status==0) {
     int size=Marshal.SizeOf(typeof(LsaString));
     for(int i=0;i<count;i++) {
      var value=(LsaString)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(LsaString));
      if(Marshal.PtrToStringUni(value.Buffer,value.Length/2)==rightName) present=true;
     }
    } else if(status!=0xc0000034) Check(status);
    if(remove && !present || !remove && present) return false;
    string name=rightName; IntPtr text=Marshal.StringToHGlobalUni(name);
    try {
     var rights=new[]{new LsaString { Buffer=text, Length=(ushort)(name.Length*2), MaximumLength=(ushort)((name.Length+1)*2) }};
     Check(remove ? LsaRemoveAccountRights(policy,bytes,false,rights,1) : LsaAddAccountRights(policy,bytes,rights,1));
    } finally { Marshal.FreeHGlobal(text); }
    return true;
   } finally { if(buffer!=IntPtr.Zero)LsaFreeMemory(buffer); LsaClose(policy); }
  }
 }
}