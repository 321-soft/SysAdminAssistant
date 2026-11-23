using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task SaveExcel(string kind,bool template)
    {
        if(kind=="employees"&&!await UnlockInventory())return;
        var dialog=new SaveFileDialog{Title=template?"Скачать шаблон Excel":"Экспорт в Excel",Filter="Книга Excel (*.xlsx)|*.xlsx",DefaultExt=".xlsx",AddExtension=true,FileName=template?$"SAA-template-{kind}.xlsx":$"SAA-{kind}-{DateTime.Now:yyyyMMdd}.xlsx",OverwritePrompt=true};
        if(dialog.ShowDialog(this)!=true)return;
        if(kind=="vault"&&!template){if(!await UnlockOrganizationVault())return;if(!Confirm(this,"Excel будет содержать открытые пароли. Сохранить файл в выбранное место?"))return;}
        var file=await remote.GetAsync<ExcelFile>($"api/excel/{kind}/{(template?"template":"export")}",lifetime.Token);
        var temp=dialog.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{await File.WriteAllBytesAsync(temp,file.File,lifetime.Token);File.Move(temp,dialog.FileName,true);}
        finally{CryptographicOperations.ZeroMemory(file.File);if(File.Exists(temp))File.Delete(temp);}
        AppMessageBox.Show(this,template?"Шаблон сохранён. Заполните лист «Данные»; подсказки находятся на листе «Инструкция».":"Файл Excel сохранён.","Excel");
    }
    private async Task ImportExcel(string kind)
    {
        if(kind=="employees"&&!await UnlockInventory())return;
        var dialog=new OpenFileDialog{Title="Загрузить из Excel",Filter="Книга Excel (*.xlsx)|*.xlsx",CheckFileExists=true,Multiselect=false};if(dialog.ShowDialog(this)!=true)return;
        if(new FileInfo(dialog.FileName).Length>ExcelExchange.MaxBytes)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Файл Excel превышает 8 МБ"));
        if(kind=="vault"&&!await UnlockOrganizationVault())return;
        var options=new FormDialog(this,"Импорт Excel").Note("Сначала покажем предпросмотр. Ошибки в любой строке отменяют весь импорт. Записи не удаляются. Для добавления оставьте ID и Версию пустыми.").Toggle("update","Обновлять существующие записи по ID из выгрузки",false);
        if(options.ShowDialog()!=true)return;
        var bytes=await File.ReadAllBytesAsync(dialog.FileName,lifetime.Token);
        try{
            var request=new ExcelRequest(bytes,options.Checked("update"));var preview=await remote.PostAsync<ExcelRequest,ExcelPreview>($"api/excel/{kind}/preview",request,lifetime.Token);
            var table=Table(preview.Rows,("Строка","Row",null),("Запись","Name",null),("Действие","Operation",null),("Ошибка","Error",null));
            var root=new DockPanel{Margin=new Thickness(20)};var note=new TextBlock{Text=$"Строк: {preview.Rows.Length} · Добавить: {preview.Rows.Count(x=>x.Operation=="Добавить")} · Обновить: {preview.Rows.Count(x=>x.Operation=="Обновить")} · Ошибок: {preview.Rows.Count(x=>x.Error.Length>0)}",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
            DockPanel.SetDock(note,Dock.Top);root.Children.Add(note);var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};var cancel=UiButton("Закрыть");var accept=UiButton("Импортировать");accept.IsEnabled=preview.Valid;footer.Children.Add(cancel);footer.Children.Add(accept);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);root.Children.Add(table);
            var window=new DialogWindow{Owner=this,Title="Предпросмотр импорта Excel",Width=960,Height=620,Content=root,WindowStartupLocation=WindowStartupLocation.CenterOwner};cancel.Click+=(_,_)=>window.DialogResult=false;accept.Click+=(_,_)=>window.DialogResult=true;
            if(window.ShowDialog()!=true)return;
            await remote.PostAsync<ExcelRequest,ExcelPreview>($"api/excel/{kind}/import",request,lifetime.Token);await remote.RefreshAsync(lifetime.Token);await ShowPage(page);AppMessageBox.Show(this,$"Импортировано строк: {preview.Rows.Length}","Excel");
        }finally{CryptographicOperations.ZeroMemory(bytes);}
    }
}
