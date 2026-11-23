using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private InvoiceView[] invoiceCache=[];
    private async Task<T[]> OptionalInvoiceIndex<T>(string path){try{return await remote.GetAsync<T[]>(path,lifetime.Token);}catch(System.Net.Http.HttpRequestException){return [];}catch(InvalidOperationException e)when(e.Message.StartsWith("Сервер: 404",StringComparison.Ordinal)||e.Message.StartsWith("Сервер: 500",StringComparison.Ordinal)){return [];}}
    private async Task<InvoiceView[]> LoadInvoices(){invoiceCache=await remote.GetAsync<InvoiceView[]>("api/invoices",lifetime.Token);return invoiceCache;}
    private async Task UploadInvoice(){var open=new OpenFileDialog{Title="Распознать счёт",Filter="PDF и изображения|*.pdf;*.png;*.jpg;*.jpeg",CheckFileExists=true};if(open.ShowDialog(this)!=true)return;if(new FileInfo(open.FileName).Length>8000000)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("До 8 МБ"));var id=await remote.PostAsync<ExcelFile,int>("api/invoices/upload",new(await File.ReadAllBytesAsync(open.FileName,lifetime.Token),Path.GetFileName(open.FileName)),lifetime.Token);await ReviewInvoice(id,this);await remote.RefreshAsync(lifetime.Token);await ShowPage(3);}
    private async Task DownloadInvoice(int paymentId)
    {
        var views=await remote.GetAsync<InvoiceView[]>("api/invoices?paymentId="+paymentId,lifetime.Token);await DownloadFiles(views.SelectMany(x=>x.Files.Select(f=>new Choice(f.Id,$"Счёт {x.Data.Number} · {f.Name}"))).ToArray());
    }
    private async Task DownloadFiles(Choice[] files)
    {
        if(files.Length==0)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("В письме нет сохранённого PDF / изображения"));var owner=Application.Current.Windows.OfType<Window>().LastOrDefault(w=>w.IsActive)??this;var form=new FormDialog(owner,"Скачать документ").Select("file","Документ",files,files[0].Key).Toggle("pdf","Сохранить как PDF (изображение будет преобразовано)",true).Note("PDF из письма сохраняется без изменений. Изображение при выборе PDF будет преобразовано; оригинал можно скачать отдельно.");if(form.ShowDialog()!=true)return;
        var id=form.Selected<int>("file");var f=await remote.GetAsync<ExcelFile>($"api/invoices/{(form.Checked("pdf")?"pdf":"file")}/{id}",lifetime.Token);
        var save=new SaveFileDialog{Title="Сохранить счет",FileName=Path.GetFileName(f.Name),Filter=form.Checked("pdf")?"Документ PDF (*.pdf)|*.pdf":"Оригинал документа (*.*)|*.*",DefaultExt=form.Checked("pdf")?".pdf":Path.GetExtension(f.Name),AddExtension=true,OverwritePrompt=true};if(save.ShowDialog(owner)!=true)return;var temp=save.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temp,f.File,lifetime.Token);File.Move(temp,save.FileName,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private DataGridColumn InvoiceDownloadColumn()
    {
        var b=new FrameworkElementFactory(typeof(Button));b.SetValue(ContentControl.ContentProperty,"PDF ↓");b.SetValue(Control.PaddingProperty,new Thickness(6,3,6,3));b.SetBinding(UIElement.VisibilityProperty,new Binding("HasDocument"){Converter=new BooleanToVisibilityConverter()});b.AddHandler(Button.ClickEvent,new RoutedEventHandler(async(sender,e)=>{e.Handled=true;if(((FrameworkElement)sender).DataContext is PaymentRow row)await Run(()=>DownloadInvoice(row.Entity.Id));}));return new DataGridTemplateColumn{Header="Счёт",Width=76,CellTemplate=new DataTemplate{VisualTree=b}};
    }
    private sealed record InvoiceListRow(InvoiceView Invoice){public string Supplier=>Invoice.Data.Supplier;public string Number=>Invoice.Data.Number;public string Amount=>Invoice.Data.AmountCents is long c?$"{c/100m:N2} {Invoice.Data.Currency}":"Проверить";public DateTime? Date=>Invoice.Data.InvoiceDate;public string Issues=>string.Join("; ",Invoice.Data.Issues);public string Status=>Invoice.Status switch{"Review"=>"Требует проверки","Added"=>"Разовый платёж создан","Linked"=>"Прикреплён к платежу","Duplicate"=>"Дубликат · платёж не создан",_=>"Пропущен"};}
    private async Task ShowInvoiceQueue()
    {
        var rows=(await LoadInvoices()).OrderBy(x=>x.Status=="Review"?0:1).ThenByDescending(x=>x.Id).Select(x=>new InvoiceListRow(x));var table=Table(rows,("Поставщик","Supplier",null),("Номер","Number",null),("Сумма","Amount",null),("Дата счёта","Date","dd.MM.yyyy"),("Статус","Status",null),("Проверить","Issues",null));var window=new DialogWindow{Owner=this,Title="Счета из почты · проверка и архив",Width=1150,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var open=UiButton("Открыть / проверить");var file=UiButton("Скачать PDF / оригинал");var skip=UiButton("Пропустить");skip.IsEnabled=AccessRules.Level(remote.CurrentUser,"payments")>=2;
        open.Click+=async(_,_)=>{if(table.SelectedItem is InvoiceListRow row)await Run(async()=>{await ReviewInvoice(row.Invoice.Id,window);table.ItemsSource=(await LoadInvoices()).Select(x=>new InvoiceListRow(x));});};file.Click+=async(_,_)=>{if(table.SelectedItem is InvoiceListRow row)await Run(()=>DownloadFiles(row.Invoice.Files.Select(f=>new Choice(f.Id,f.Name)).ToArray()));};skip.Click+=async(_,_)=>{if(table.SelectedItem is InvoiceListRow row&&row.Invoice.Status=="Review")await Run(async()=>{await remote.PostAsync<VaultTarget,bool>("api/invoices/ignore",new(row.Invoice.Id,row.Invoice.Revision),lifetime.Token);table.ItemsSource=(await LoadInvoices()).Select(x=>new InvoiceListRow(x));});};table.SetValue(HasCustomDoubleClickProperty,true);table.MouseDoubleClick+=(_,_)=>open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));window.Content=ListSurface(table,[open,file,skip],search:true);window.ShowDialog();await remote.RefreshAsync(lifetime.Token);await ShowPage(3);
    }
    private async Task ReviewInvoice(int id,Window owner)
    {
        var v=await remote.GetAsync<InvoiceView>("api/invoices/"+id,lifetime.Token);var d=v.Data;var form=new FormDialog(owner,"Счёт "+d.Number,1180).Note(string.Join("\n",d.Issues)+(d.DuplicateOfId is int original?"\nДубликат счёта #"+original:"")).Text("supplier","Поставщик",d.Supplier).Text("number","Номер счета",d.Number).Text("date","Дата счёта · ДД.ММ.ГГГГ",d.InvoiceDate?.ToString("dd.MM.yyyy")??"").Text("due","Оплатить до · ДД.ММ.ГГГГ",d.DueDate?.ToString("dd.MM.yyyy")??"").Text("amount","Сумма",d.AmountCents is long c?(c/100m).ToString("0.00"):"").Text("currency","Валюта · RUB / USD / EUR / AED…",d.Currency).Text("inn","ИНН / идентификатор",d.TaxId).Text("kpp","КПП",d.Kpp).Text("account","Расчётный счёт",d.BankAccount).Text("bik","БИК / код банка",d.BankCode).Text("correspondent","Корреспондентский счёт",d.Correspondent).Text("purpose","Назначение",d.Purpose,true).Note($"От: {d.Sender}\nТема: {d.Subject}\nIMAP: {d.ReceivedUtc.ToServerTime():dd.MM.yyyy HH:mm}\nДата отправителя: {d.SenderDateUtc?.ToServerTime():dd.MM.yyyy HH:mm}\nИзвлечение: {d.Extraction}"+(d.CalculatedDue?"\nСрок рассчитан по правилу поставщика":""))
            .ActionAsync("Скачать PDF / оригинал",()=>DownloadFiles(v.Files.Select(f=>new Choice(f.Id,f.Name)).ToArray()))
            .Action("Показать исходное письмо",()=>new FormDialog(owner,"Письмо · текстовый просмотр",900).Note("От: "+d.Sender+"\nТема: "+d.Subject).Text("mail","Тело письма · без внешних изображений",d.MailBody,true,true).ViewOnly().ShowDialog()).TextPreview("ТЕКСТОВЫЙ ПРЕДПРОСМОТР\nОригинальный PDF — кнопка скачивания.\n\n"+d.RawText);
        if(d.LinkedPeriod is DateTime linkedPeriod)form.Note("Прикреплено к периоду: "+linkedPeriod.ToString("dd.MM.yyyy"));
        if(v.Status!="Review"||AccessRules.Level(remote.CurrentUser,"payments")<2){form.ViewOnly().ShowDialog();return;}
        await using var db=factory.Create();var payments=await db.Payments.AsNoTracking().OrderBy(x=>x.Title).ToArrayAsync();form.Select("link","Действие",new[]{new Choice(null,"Создать отдельный разовый платёж")}.Concat(payments.Select(p=>new Choice(p.Id,"Прикрепить к «"+p.Title+"» · текущий срок "+Display.Date(p.DueDate)))),null).Text("period","Период существующего платежа · ДД.ММ.ГГГГ",d.DueDate?.ToString("dd.MM.yyyy")??"").Note(d.RecurringCandidateId is int cid?"Возможное совпадение: "+payments.FirstOrDefault(p=>p.Id==cid)?.Title:"Прикрепление не меняет расписание и не отмечает платёж оплаченным.").Submit("Подтвердить");
        form.Validate=()=>{_=form.Date("date");_=form.Date("due");return InvoiceRecognition.Amount(form.Value("amount"))==null?"Проверьте сумму":Required(form.Value("supplier"),form.Value("number"),form.Value("currency"));};if(form.ShowDialog()!=true)return;
        d.Supplier=form.Value("supplier");d.Number=form.Value("number");d.InvoiceDate=form.Date("date");d.DueDate=form.Date("due");d.AmountCents=InvoiceRecognition.Amount(form.Value("amount"));d.Currency=form.Value("currency").Trim().ToUpperInvariant();d.TaxId=form.Value("inn");d.Kpp=form.Value("kpp");d.BankAccount=form.Value("account");d.BankCode=form.Value("bik");d.Correspondent=form.Value("correspondent");d.Purpose=form.Value("purpose");d.CalculatedDue=false;var link=form.Selected("link") as int?;var period=form.Value("period").Length==0?(DateTime?)null:form.Date("period");
        await remote.PostAsync<InvoiceApproval,bool>("api/invoices/approve",new(v.Id,v.Revision,d,link,period),lifetime.Token);
    }
}
