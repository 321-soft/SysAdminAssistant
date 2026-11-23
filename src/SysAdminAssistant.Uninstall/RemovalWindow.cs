namespace SysAdminAssistant.Uninstall;
internal sealed class RemovalWindow:Form
{
    private readonly bool english;
    private string T(string ru,string en)=>english?en:ru;
    private readonly ComboBox component=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=620};
    private readonly TextBox folder=new(){Width=500};
    private readonly TextBox details=new(){ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Height=170,Dock=DockStyle.Fill};
    private readonly CheckBox database=new(){AutoSize=true},data=new(){AutoSize=true},account=new(){AutoSize=true};
    private readonly Button remove=new(){AutoSize=true};private bool busy;
    public RemovalWindow(string[] args,bool en)
    {
        english=en;Text=T("Удаление SysAdmin Assistant","Uninstall SysAdmin Assistant");ClientSize=new(760,590);MinimumSize=new(680,560);StartPosition=FormStartPosition.CenterScreen;Font=new("Segoe UI",10);BackColor=System.Drawing.Color.FromArgb(245,248,254);Padding=new(20);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8};layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text=T("Выберите компонент для удаления","Select a component to remove"),AutoSize=true,Font=new("Segoe UI",18)},0,0);
        component.Items.AddRange([T("Сервер","Server"),T("Клиент","Desktop client"),T("Агент устройств / серверов","Device / server agent"),T("Агент рабочих станций","Workstation agent")]);layout.Controls.Add(component,0,1);
        layout.Controls.Add(new Label{AutoSize=true,Text=T("Папка данных сервера (для старой установки можно выбрать вручную)","Server data folder (select manually for a legacy installation)")},0,2);
        var pick=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill};var browse=new Button{Text=T("Обзор…","Browse…"),AutoSize=true};var refresh=new Button{Text=T("Проверить","Inspect"),AutoSize=true};pick.Controls.AddRange([folder,browse,refresh]);layout.Controls.Add(pick,0,3);layout.Controls.Add(details,0,4);
        database.Text=T("Безвозвратно удалить выбранную базу сервера","Permanently delete the selected server database");data.Text=T("Также удалить локальные данные и ключи выбранного компонента","Also delete local data and keys for the selected component");account.Text=T("Удалить служебную учётную запись сервера, если она больше не используется","Delete the server's managed account if no longer used");
        var options=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.TopDown,Dock=DockStyle.Fill};options.Controls.AddRange([database,data,account]);layout.Controls.Add(options,0,5);
        layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new(700,0),Text=T("По умолчанию данные сохраняются. Проверьте резервную копию перед удалением базы. Агентов после удаления можно отозвать в Assistant.","Data is kept by default. Verify a backup before deleting a database. Removed agents can be revoked in Assistant.")},0,6);
        var buttons=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Dock=DockStyle.Fill};var close=new Button{Text=T("Закрыть","Close"),AutoSize=true};remove.Text=T("Удалить…","Uninstall…");buttons.Controls.AddRange([close,remove]);layout.Controls.Add(buttons,0,7);Controls.Add(layout);
        close.Click+=(_,_)=>Close();browse.Click+=(_,_)=>{using var dialog=new FolderBrowserDialog();if(dialog.ShowDialog(this)==DialogResult.OK){folder.Text=dialog.SelectedPath;Inspect();}};refresh.Click+=(_,_)=>Inspect();component.SelectedIndexChanged+=(_,_)=>{database.Checked=data.Checked=account.Checked=false;browse.Enabled=Kind=="server";folder.Text=RemovalEngine.DefaultData(Kind);Inspect();};
        remove.Click+=async(_,_)=>await RemoveAsync();FormClosing+=(_,e)=>e.Cancel=busy;
        var index=Array.IndexOf(args,"--component");var sourceIndex=Array.IndexOf(args,"--source-dir");var source=sourceIndex>=0&&sourceIndex+1<args.Length?args[sourceIndex+1]:AppContext.BaseDirectory;
        var parent=Path.GetFileName(Path.TrimEndingDirectorySeparator(source));if(parent.Equals("Uninstall",StringComparison.OrdinalIgnoreCase))parent=Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source)));
        var inferred=parent=="SysAdmin Assistant Workstation"?"workstation":parent=="SysAdmin Assistant Agent"?"agent":parent=="SysAdmin Assistant"?"client":"server";
        var kind=index>=0&&index+1<args.Length?args[index+1]:inferred;component.SelectedIndex=kind switch{"client"=>1,"agent"=>2,"workstation"=>3,_=>0};
    }
    private string Kind=>component.SelectedIndex switch{1=>"client",2=>"agent",3=>"workstation",_=>"server"};
    private void Inspect(){database.Enabled=account.Enabled=folder.Enabled=Kind=="server";try{details.Text=RemovalEngine.Inspect(Kind,folder.Text,english).Replace("\n","\r\n");remove.Enabled=true;}catch(Exception e){details.Text=e.Message;remove.Enabled=false;}}
    private async Task RemoveAsync()
    {
        var kind=Kind;var selected=folder.Text;
        try{
            var plan=RemovalEngine.Plan(kind,selected,database.Checked,data.Checked,account.Checked,english);
            var review=plan.Summary+"\n\n"+T("Продолжить?","Continue?");
            if(MessageBox.Show(this,review,Text,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            if(database.Checked&&MessageBox.Show(this,T("База данных будет удалена БЕЗВОЗВРАТНО. Подтвердить удаление?","The database will be PERMANENTLY DELETED. Confirm deletion?"),Text,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            busy=true;component.Enabled=remove.Enabled=false;UseWaitCursor=true;
            await Task.Run(()=>RemovalEngine.Remove(plan));
            details.Text=T("Удаление завершено. Сохранённые данные можно использовать при повторной установке.","Removal completed. Retained data can be used when reinstalling.");remove.Enabled=false;
        }catch(Exception e){MessageBox.Show(this,T("Удаление остановлено. Часть действий могла выполниться.\n","Removal stopped. Some operations may have completed.\n")+e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);remove.Enabled=true;}
        finally{busy=false;component.Enabled=true;UseWaitCursor=false;}
    }
}
