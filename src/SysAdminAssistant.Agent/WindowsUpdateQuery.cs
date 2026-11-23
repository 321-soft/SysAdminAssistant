using System.Runtime.InteropServices;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Agent;
internal static class WindowsUpdateQuery
{
 private static void Release(object? o){try{if(o!=null&&Marshal.IsComObject(o))Marshal.FinalReleaseComObject(o);}catch(InvalidComObjectException){}}
 private static string Text(object? o,int limit)=>WindowsUpdateResults.Clean(Convert.ToString(o)??"",limit);
 private static string Error(string stage,Exception e)=>stage+": "+e.GetType().Name+" 0x"+e.HResult.ToString("X8");
 public static OsUpdateStatus Read(){object? session=null,searcher=null,result=null,updates=null,auto=null,results=null,system=null;string stage="Создание WUA",installed=RuntimeInformation.OSDescription;DateTime? checkedAt=null;var diagnostics=new List<string>();var names=new List<string>();int total=0;bool complete=false,reboot=false;
 try{
 session=Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")??throw new PlatformNotSupportedException(global::SysAdminAssistant.Core.ProductText.Get("WUA не зарегистрирован")));searcher=((dynamic)session!).CreateUpdateSearcher();((dynamic)searcher).Online=false;((dynamic)searcher).CanAutomaticallyUpgradeService=false;
 stage="Чтение локального списка WUA";result=((dynamic)searcher).Search("IsInstalled=0 and IsHidden=0 and Type='Software'");int code=(int)((dynamic)result).ResultCode;complete=code==2;if(!complete)diagnostics.Add("WUA ResultCode="+code);updates=((dynamic)result).Updates;int count=(int)((dynamic)updates).Count;
 if(count>1000){diagnostics.Add("Список превышает лимит 1000");complete=false;}
 for(int i=0;i<Math.Min(count,1000);i++){object? u=null;try{u=((dynamic)updates)[i];total++;if(names.Count<20){string title=Text(((dynamic)u).Title,200);object? ids=null;try{ids=((dynamic)u).KBArticleIDs;var kb=new List<string>();for(int j=0;j<Math.Min((int)((dynamic)ids).Count,4);j++)kb.Add("KB"+Text(((dynamic)ids)[j],20));if(kb.Count>0&&!kb.Any(k=>title.Contains(k,StringComparison.OrdinalIgnoreCase)))title=string.Join(",",kb)+" · "+title;}catch(Exception e){diagnostics.Add(Error("Номер KB",e));}finally{Release(ids);}names.Add(Text(title,200));}}catch(Exception e){complete=false;diagnostics.Add(Error("Запись обновления",e));}finally{Release(u);}}
 }catch(Exception e){diagnostics.Add(Error(stage,e));}
 // Date and reboot metadata must never erase an already discovered update list.
 try{auto=Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate")!);results=((dynamic)auto!).Results;checkedAt=WindowsUpdateResults.Date(((dynamic)results).LastSearchSuccessDate);}catch(Exception e){diagnostics.Add(Error("Дата поиска WUA",e));}
 try{system=Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo")!);reboot=(bool)((dynamic)system!).RebootRequired;}catch(Exception e){diagnostics.Add(Error("Перезагрузка WUA",e));}
 foreach(var o in new[]{system,results,auto,updates,result,searcher,session})Release(o);
 var detail="Локальный кэш Windows Update/WSUS, включая необязательные software-обновления; без сетевого поиска"+(diagnostics.Count>0?"; "+string.Join("; ",diagnostics.Take(4)):"");return WindowsUpdateResults.Build(installed,names,total,checkedAt,complete,reboot,detail);
 }
}
