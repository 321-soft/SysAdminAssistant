using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private static string PeripheralKind(string kind)=>kind switch {
        "Display"=>"Монитор", "Keyboard"=>"Клавиатура", "Mouse"=>"Мышь", "Touch"=>"Сенсорное устройство",
        "Printer"=>"Принтер", "PrintQueue"=>"Очередь печати", "Scanner"=>"Сканер", "Camera"=>"Камера",
        "Audio"=>"Аудиоустройство", "Dock"=>"Док-станция", "Storage"=>"USB-накопитель", "SmartCard"=>"Картридер смарт-карт",
        "GameController"=>"Игровой контроллер", "Ups"=>"ИБП", _=>"USB-устройство"
    };
    private static string PeripheralPlacement(string placement)=>placement switch {
        "BuiltIn"=>"Встроенное", "External"=>"Внешнее", "Virtual"=>"Виртуальное",
        "Network"=>"Сетевое", _=>"Не определено"
    };
    private void AddStationPeripherals(FormDialog dialog,StationPeripheralScan? scan)
    {
        dialog.Tab("Подключённое оборудование");
        if(scan==null)
        {
            dialog.Note("Данные ещё не получены. Сбор поддерживают агенты 0.46.1 и новее.");
            return;
        }
        dialog.Fact(scan.AttemptedUtc!=null?"Последний успешный сбор":"Последняя проверка",Display.Stamp(AppTime.FromUtc(scan.CapturedUtc)));
        if(scan.AttemptedUtc is DateTime attempted)dialog.Fact("Последняя попытка",Display.Stamp(AppTime.FromUtc(attempted)));
        if(scan.Partial&&scan.Devices.Length>0)dialog.Note(scan.AttemptedUtc!=null?"Отображён последний полученный список. Новый опрос завершился ошибкой.":"Часть оборудования не удалось прочитать.");
        if(scan.Devices.Length==0)dialog.Note(scan.Partial?"Не удалось получить список оборудования.":"Устройства не обнаружены.");
        if(scan.Errors is {Length:>0})dialog.Action("Подробности диагностики",()=>{
            var details=string.Join("\n",scan.Errors);
            new FormDialog(dialog,"Диагностика сбора оборудования",850).Text("details","Ошибка и запрос",details,true,true).TextHeight("details",300)
                .Action("Копировать",()=>System.Windows.Clipboard.SetText(details)).ViewOnly().ShowDialog();
        });
        else dialog.Fact("Устройства",string.Join("\n",scan.Devices.Select(d=>string.Join(" · ",
            new[]{PeripheralKind(d.Kind),d.Name,PeripheralPlacement(d.Placement),d.Connection,
                  d.Serial.Length>0?"SN: "+d.Serial:""}.Where(x=>x.Length>0)))));
        var physical=scan.Devices.Where(x=>x.Kind!="PrintQueue"&&x.Placement is not ("Virtual" or "Network")).ToArray();
        if(physical.Length>0&&AccessRules.Level(remote.CurrentUser,"inventory")>=2)
        {
            dialog.Select("peripheral-asset","Устройство для инвентаризации",
                physical.Select((x,i)=>new Choice(i,PeripheralKind(x.Kind)+" · "+x.Name+(x.Serial.Length>0?" · "+x.Serial:""))),0)
                .ActionAsync("Создать карточку имущества…",async ()=>{
                    var selected=physical[dialog.Selected<int>("peripheral-asset")];
                    if(!await UnlockInventory())return;
                    await EditAsset(new InventoryAsset {
                        Category=PeripheralKind(selected.Kind), Manufacturer=selected.Manufacturer,
                        Model=selected.Name, SerialNumber=selected.Serial, DiskType=""
                    });
                });
        }
        if(physical.Length>0)
        {
            dialog.Select("peripheral-photo","Устройство для поиска фото",
                physical.Select((x,i)=>new Choice(i,PeripheralKind(x.Kind)+" · "+x.Name)),0)
                .Action("Найти фото…",()=>{
                    var device=physical[dialog.Selected<int>("peripheral-photo")];
                    var search=new FormDialog(dialog,"Поиск фото модели",560)
                        .Note("Уточните название модели. В браузер передаются только эти два поля.")
                        .Text("brand","Производитель",device.Manufacturer)
                        .Text("model","Модель",device.Name.Contains("HID",StringComparison.OrdinalIgnoreCase)||
                            device.Name.Contains("Receiver",StringComparison.OrdinalIgnoreCase)?"":device.Name)
                        .Submit("Найти фото");
                    search.Validate=()=>string.IsNullOrWhiteSpace(search.Value("brand"))||string.IsNullOrWhiteSpace(search.Value("model"))
                        ?"Укажите производителя и точную модель.":null;
                    if(search.ShowDialog()==true)FindModelPhoto(new(PeripheralKind(device.Kind),search.Value("brand"),search.Value("model")),device.Serial);
                });
        }
    }
}
