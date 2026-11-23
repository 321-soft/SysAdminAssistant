using System.Runtime.InteropServices;
namespace SysAdminAssistant.Desktop;
internal static class WorkstationMsi
{
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern uint MsiOpenDatabaseW(string path,nint mode,out uint db);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern uint MsiDatabaseOpenViewW(uint db,string query,out uint view);
    [DllImport("msi.dll")]static extern uint MsiCreateRecord(uint count);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern uint MsiRecordSetStringW(uint record,uint field,string value);
    [DllImport("msi.dll")]static extern uint MsiViewExecute(uint view,uint record);
    [DllImport("msi.dll")]static extern uint MsiViewFetch(uint view,out uint record);
    [DllImport("msi.dll")]static extern uint MsiDatabaseCommit(uint db);
    [DllImport("msi.dll")]static extern uint MsiCloseHandle(uint handle);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern uint MsiGetSummaryInformationW(uint db,string? path,uint count,out uint summary);
    [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern uint MsiSummaryInfoSetPropertyW(uint summary,uint property,uint type,int number,nint time,string value);
    [DllImport("msi.dll")]static extern uint MsiSummaryInfoPersist(uint summary);
    private static void Check(uint status){if(status!=0)throw new System.ComponentModel.Win32Exception((int)status);}
    public static void Configure(string path,string url,string pin,string authority,string enrollment)
    {
        Check(MsiOpenDatabaseW(path,1,out var db));
        try{
            foreach(var (id,value) in new[]{("StationUrl",url),("StationPin",pin),("StationAuthority",authority),("StationEnrollment",enrollment)}){
                Check(MsiDatabaseOpenViewW(db,"SELECT `Registry` FROM `Registry` WHERE `Registry` = '"+id+"'",out var check));
                try{Check(MsiViewExecute(check,0));Check(MsiViewFetch(check,out var match));MsiCloseHandle(match);}
                finally{MsiCloseHandle(check);}
                Check(MsiDatabaseOpenViewW(db,"UPDATE `Registry` SET `Value` = ? WHERE `Registry` = '"+id+"'",out var view));
                var record=MsiCreateRecord(1);
                try{
                    if(record==0)throw new InvalidOperationException("Не удалось открыть параметры MSI");
                    Check(MsiRecordSetStringW(record,1,value));Check(MsiViewExecute(view,record));
                }finally{if(record!=0)MsiCloseHandle(record);MsiCloseHandle(view);}
            }
            Check(MsiGetSummaryInformationW(db,null,1,out var summary));
            try{Check(MsiSummaryInfoSetPropertyW(summary,9,30,0,0,Guid.NewGuid().ToString("B").ToUpperInvariant()));Check(MsiSummaryInfoPersist(summary));}
            finally{MsiCloseHandle(summary);}
            Check(MsiDatabaseCommit(db));
        }finally{MsiCloseHandle(db);}
    }
}
