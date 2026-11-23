using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task ApplyBatch<T>(IEnumerable<T> entries,Func<T,Task> apply)
    {
        int success=0;var errors=new List<string>();
        foreach(var entry in entries){
            try{await apply(entry);success++;}
            catch(Exception e)when(e is not OperationCanceledException){var name=entry switch{StationView station=>station.Name,Device device=>device.Name,DeviceRegistryRow row=>row.Name,_=>"Запись"};errors.Add(name+": "+e.Message);}
        }
        if(errors.Count>0)AppMessageBox.Show(this,$"Выполнено: {success}. Ошибок: {errors.Count}.\n"+string.Join("\n",errors.Take(10)),"Результат действий");
    }
}
