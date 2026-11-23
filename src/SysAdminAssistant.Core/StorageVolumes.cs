namespace SysAdminAssistant.Core;
public static class StorageVolumes
{
    public static bool Include(StationVolume volume,ISet<string> identities)=>volume.AliasOf.Length==0&&identities.Add(volume.Identity);
}
