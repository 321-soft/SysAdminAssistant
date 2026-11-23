using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
internal sealed class HelpWindow:DialogWindow
{
 public HelpWindow(string topic="overview")
 {
  Title="Справка SysAdmin Assistant";Width=1060;Height=750;MinWidth=620;MinHeight=420;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var root=new DockPanel{Margin=new Thickness(8)};var tools=new DockPanel{Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(tools,Dock.Top);root.Children.Add(tools);var export=new Button{Content=ProductText.Get("Сохранить руководство…"),Margin=new Thickness(12,0,0,0)};DockPanel.SetDock(export,Dock.Right);tools.Children.Add(export);var search=new TextBox{Margin=new Thickness(0,0,0,12),ToolTip="Поиск по справке"};DockPanel.SetDock(search,Dock.Top);tools.Children.Add(search);
  var footer=new TextBlock{Text="SysAdmin Assistant · "+typeof(ProductHelp).Assembly.GetName().Version?.ToString(3)+" · assistant.321.su · support@321.su",Margin=new Thickness(0,12,0,0),FontSize=11};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
  var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(270)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(6)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var list=new ListBox{DisplayMemberPath="Title",Margin=new Thickness(0,0,12,0)};grid.Children.Add(list);
  var splitter=new GridSplitter{Width=6,HorizontalAlignment=HorizontalAlignment.Stretch};Grid.SetColumn(splitter,1);grid.Children.Add(splitter);
  var body=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(12,0,0,0),FontSize=14};LanguageUi.Preserve(body);Grid.SetColumn(body,2);grid.Children.Add(body);root.Children.Add(grid);Content=root;
  export.Click+=(_,_)=>{var save=new Microsoft.Win32.SaveFileDialog{Filter="Markdown (*.md)|*.md",FileName="SysAdminAssistant_UserGuide_"+ProductText.Language+".md"};if(save.ShowDialog(this)!=true)return;try{System.IO.File.WriteAllText(save.FileName,"# SysAdmin Assistant\n\n"+string.Join("\n\n",ProductHelp.All.Select(a=>"## "+a.Title+"\n\n"+a.Body)),System.Text.Encoding.UTF8);}catch(Exception error){AppMessageBox.Show(this,error.Message,Title);}};
  list.SelectionChanged+=(_,_)=>body.Text=list.SelectedItem is HelpArticle article?article.Title+"\n\n"+article.Body:ProductText.Get("Ничего не найдено");
  void Filter(){var old=(list.SelectedItem as HelpArticle)?.Id;list.ItemsSource=ProductHelp.Search(search.Text).ToArray();list.SelectedItem=list.Items.Cast<HelpArticle>().FirstOrDefault(a=>a.Id==old)??list.Items.Cast<HelpArticle>().FirstOrDefault();}
  search.TextChanged+=(_,_)=>Filter();Filter();list.SelectedItem=list.Items.Cast<HelpArticle>().FirstOrDefault(a=>a.Id==topic)??list.SelectedItem;
 }
 public static void Register(){EventManager.RegisterClassHandler(typeof(Window),Keyboard.PreviewKeyDownEvent,new KeyEventHandler((sender,e)=>{if(e.Key!=Key.F1||sender is HelpWindow)return;e.Handled=true;var owner=(Window)sender;var topic=Application.Current.MainWindow is MainWindow main?main.CurrentHelpTopic:"connection";new HelpWindow(topic){Owner=owner}.ShowDialog();}));}
}
