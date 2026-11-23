using System.Windows;
using System.Windows.Controls;
using SysAdminAssistant.Core;
using SysAdminAssistant.Desktop;
internal static class Program
{
    [STAThread]private static void Main(string[] args)
    {
        if(args.Contains("--hardware-smoke")){
            var assembly=System.Reflection.Assembly.Load("SysAdminAssistant.Agent");
            var type=assembly.GetType("SysAdminAssistant.Agent.StationPeripheralCollector",true)!;
            var caption=(string)type.GetMethod("ProbeWmi",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public)!.Invoke(null,null)!;
            Check("Real WMI reads an operating system property",!string.IsNullOrWhiteSpace(caption));
            var scan=(StationPeripheralScan)type.GetMethod("Read",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public)!.Invoke(null,null)!;
            Check("Peripheral collection is not completely unavailable",scan.Devices.Length>0||!scan.Partial);
            foreach(var error in scan.Errors??[])Console.WriteLine(error);
            Console.WriteLine("Collected peripherals: "+scan.Devices.Length+"; partial: "+scan.Partial);
            return;
        }
        var app=new Application();LanguageUi.Initialize("ru");TableSorting.Initialize();
        var button=new Button{Content="Рабочие станции"};var original=new TextBlock{Text="Устройства"};LanguageUi.Preserve(original);
        var table=new DataGrid();var column=new DataGridTextColumn{Header="Состояние"};table.Columns.Add(column);
        var panel=new StackPanel();panel.Children.Add(button);panel.Children.Add(original);panel.Children.Add(table);
        var window=new Window{Title="Настройки",Content=panel,Width=300,Height=220,ShowActivated=false,Left=-10000,Top=-10000};
        try{
            window.Show();
            ProductText.DefaultLanguage="en";LanguageUi.RefreshWindows();
            Check("Live client language switch translates existing buttons",Equals(button.Content,ProductText.Get("Рабочие станции"))&&!Equals(button.Content,"Рабочие станции"));
            Check("Live client language switch translates table headers",Equals(column.Header,ProductText.Get("Состояние"))&&!Equals(column.Header,"Состояние"));
            Check("Device names and raw evidence remain unchanged",original.Text=="Устройства");
            ProductText.DefaultLanguage="ru";LanguageUi.RefreshWindows();
            Check("Live language switch reverses existing English labels",Equals(button.Content,"Рабочие станции")&&Equals(column.Header,"Состояние"));
            for(int i=0;i<8;i++){ProductText.DefaultLanguage=i%2==0?"en":"ru";LanguageUi.RefreshWindows();}
            Check("Repeated switching preserves labels and raw values",Equals(button.Content,"Рабочие станции")&&original.Text=="Устройства");
            using var server=new System.Windows.Forms.Form{Text="Управление сервером · SysAdmin Assistant"};
            var start=new System.Windows.Forms.Button{Text="Запустить службу"};server.Controls.Add(start);
            ProductText.DefaultLanguage="en";SysAdminAssistant.ServerSetup.SetupTheme.Apply(server);
            Check("Live server management language translates existing service buttons",start.Text==ProductText.Get("Запустить службу")&&start.Text!="Запустить службу");
            ProductText.DefaultLanguage="ru";SysAdminAssistant.ServerSetup.SetupTheme.Apply(server);
            Check("Server management switches back without restarting",start.Text=="Запустить службу"&&server.Text=="Управление сервером · SysAdmin Assistant");
            var items=new[]{new SortRow("pc-20","50 дней"),new SortRow("pc-2","9 дней"),new SortRow("pc-11","11 дней")};
            var sorted=new TestGrid{ItemsSource=items,AutoGenerateColumns=false,SelectionMode=DataGridSelectionMode.Extended};
            var uptime=new DataGridTextColumn{Header="Аптайм",Binding=new System.Windows.Data.Binding("Uptime"),SortMemberPath="Uptime"};sorted.Columns.Add(uptime);
            sorted.SelectedItem=items[1];sorted.Sort(uptime);
            Check("Header sorting uses durations and preserves selected row identity",ReferenceEquals(sorted.Items[0],items[1])&&ReferenceEquals(sorted.SelectedItem,items[1]));
            sorted.Sort(uptime);Check("Header sorting supports descending duration order",ReferenceEquals(sorted.Items[0],items[0])&&uptime.SortDirection==System.ComponentModel.ListSortDirection.Descending);
            var saved=TableSorting.Capture(sorted);sorted.ItemsSource=items.Reverse().ToArray();TableSorting.Apply(sorted,saved);
            Check("Refresh restores the custom ordering",ReferenceEquals(sorted.Items[0],items[0]));
            // No TableSorting.Install here: the production control must own sorting before Loaded.
            var readings=new[]{new RawSortRow("9 д. 12 ч. 17 мин.",9*86400+12*3600+17*60,"0.46.9.0"),new RawSortRow("8 д. 09 ч. 10 мин.",8*86400+9*3600+10*60,"0.46.13.0"),new RawSortRow("70 д. 02 ч. 50 мин.",70*86400+2*3600+50*60,"0.46.2.0"),new RawSortRow("7 д. 03 ч. 42 мин.",7*86400+3*3600+42*60,"0.46.14.0")};
            var actual=new TestGrid{ItemsSource=readings,AutoGenerateColumns=false};
            var rawTime=new DataGridTextColumn{Binding=new System.Windows.Data.Binding("Uptime"),SortMemberPath="UptimeSort"};
            var rawAgent=new DataGridTextColumn{Binding=new System.Windows.Data.Binding("Agent"),SortMemberPath="AgentSort"};
            actual.Columns.Add(rawTime);actual.Columns.Add(rawAgent);
            actual.Sort(rawTime);Check("Production control sorts raw uptime without a Loaded event",actual.Items.Cast<RawSortRow>().Select(r=>r.UptimeSort).SequenceEqual(readings.Select(r=>r.UptimeSort).Order()));
            actual.Sort(rawTime);Check("Production control reverses raw uptime",actual.Items.Cast<RawSortRow>().Select(r=>r.UptimeSort).SequenceEqual(readings.Select(r=>r.UptimeSort).OrderDescending()));
            actual.Sort(rawAgent);Check("Production control sorts actual agent versions",actual.Items.Cast<RawSortRow>().Select(r=>r.Agent).SequenceEqual(new[]{"0.46.2.0","0.46.9.0","0.46.13.0","0.46.14.0"}));
            actual.Sort(rawAgent);Check("Production control reverses agent versions",actual.Items.Cast<RawSortRow>().First().Agent=="0.46.14.0");
            var selectedAction=new Button{Content="Создать объект инвентаризации",IsEnabled=true};
            var bar=SelectionActionBar.Create(sorted,[new Button{Content="Создать установщик MSI"}],[selectedAction]);
            var layout=new DockPanel();DockPanel.SetDock(bar,Dock.Top);layout.Children.Add(bar);layout.Children.Add(sorted);window.Content=layout;window.Height=600;
            foreach(var width in new[]{800d,390d})foreach(var language in new[]{"ru","en"}){
                window.Width=width;ProductText.DefaultLanguage=language;LanguageUi.RefreshWindows();sorted.SelectedItems.Clear();window.UpdateLayout();
                var before=sorted.TransformToAncestor(layout).Transform(new Point()).Y;
                sorted.SelectedItems.Add(items[0]);window.UpdateLayout();var chosen=sorted.TransformToAncestor(layout).Transform(new Point()).Y;
                selectedAction.IsEnabled=false;sorted.SelectedItems.Add(items[1]);window.UpdateLayout();
                var multiple=sorted.TransformToAncestor(layout).Transform(new Point()).Y;
                sorted.SelectedItems.Clear();window.UpdateLayout();var cleared=sorted.TransformToAncestor(layout).Transform(new Point()).Y;
                Check("Action bar reserves table geometry for "+width+" / "+language,Math.Abs(before-chosen)<0.1&&Math.Abs(before-multiple)<0.1&&Math.Abs(before-cleared)<0.1);
            }
            Console.WriteLine("PASS: Windows language, typed table sorting and stable action panels");
        }finally{window.Close();app.Shutdown();}
    }
    private sealed record RawSortRow(string Uptime,long UptimeSort,string Agent){public Version AgentSort=>Version.Parse(Agent);}
    private sealed record SortRow(string Name,string Uptime);
    private sealed class TestGrid:TypedDataGrid{public void Sort(DataGridColumn column)=>OnSorting(new DataGridSortingEventArgs(column));}
    private static void Check(string name,bool ok){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);}
}
