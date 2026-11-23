using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
 private void DeveloperLink_Click(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo("https://321.su"){UseShellExecute=true});}catch(Exception error){AppMessageBox.Show(this,error.Message,"321.su");}}
 private async void Language_Click(object sender,RoutedEventArgs e)
 {
  var f=new FormDialog(this,"Язык интерфейса").Select("language","Язык",new[]{new Choice("ru","Русский"),new Choice("en","English")},LanguageUi.Load())
   .Note("Язык интерфейса применяется сразу. Язык серверных уведомлений настраивается отдельно.");
  if(f.ShowDialog()!=true)return;
  var selected=f.Selected<string>("language");LanguageUi.Save(selected);
  SysAdminAssistant.Core.ProductText.DefaultLanguage=selected;
  LanguageUi.RefreshWindows();UpdateTrayLanguage();
  await Run(()=>ShowPage(page));
 }

 private void Help_Click(object sender,RoutedEventArgs e){new HelpWindow(CurrentHelpTopic){Owner=this}.ShowDialog();}
 private void Updates_Click(object sender,RoutedEventArgs e){new ProductUpdateWindow(remote,ProductUpdatesDot.Visibility==Visibility.Visible){Owner=this}.ShowDialog();
  _=CheckProductUpdatesAsync();}
 private void Agreement_Click(object sender,RoutedEventArgs e){new AgreementWindow(false){Owner=this}.ShowDialog();}
}
