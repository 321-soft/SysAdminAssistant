namespace SysAdminAssistant.Setup;
internal static class SetupAgreement
{
    internal static bool ConfirmAgent(string language)
    {
        var path=System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantAgentSetup","legal","acceptance.json");
        if(Core.LicenseAgreement.IsAccepted(path))return true;
        var en=language=="en";
        using var form=new System.Windows.Forms.Form{Text=en?"License Agreement":"Лицензионное соглашение",ClientSize=new(780,620),MinimumSize=new(600,450),StartPosition=System.Windows.Forms.FormStartPosition.CenterScreen,Font=new System.Drawing.Font("Segoe UI",10)};
        var panel=new System.Windows.Forms.TableLayoutPanel{Dock=System.Windows.Forms.DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new(16)};panel.RowStyles.Add(new(System.Windows.Forms.SizeType.Percent,100));panel.RowStyles.Add(new(System.Windows.Forms.SizeType.AutoSize));panel.RowStyles.Add(new(System.Windows.Forms.SizeType.AutoSize));
        var text=new System.Windows.Forms.TextBox{Multiline=true,ReadOnly=true,WordWrap=true,ScrollBars=System.Windows.Forms.ScrollBars.Vertical,Dock=System.Windows.Forms.DockStyle.Fill,Text=Core.LicenseAgreement.Text(language)};
        var accepted=new System.Windows.Forms.CheckBox{Text=en?"I accept the license agreement":"Я принимаю условия лицензионного соглашения",AutoSize=true};
        var buttons=new System.Windows.Forms.FlowLayoutPanel{AutoSize=true,Dock=System.Windows.Forms.DockStyle.Fill};var next=new System.Windows.Forms.Button{Text=en?"Continue":"Продолжить",Enabled=false,AutoSize=true,DialogResult=System.Windows.Forms.DialogResult.OK};var cancel=new System.Windows.Forms.Button{Text=en?"Cancel":"Отмена",AutoSize=true,DialogResult=System.Windows.Forms.DialogResult.Cancel};buttons.Controls.AddRange([next,cancel]);accepted.CheckedChanged+=(_,_)=>next.Enabled=accepted.Checked;
        panel.Controls.Add(text,0,0);panel.Controls.Add(accepted,0,1);panel.Controls.Add(buttons,0,2);form.Controls.Add(panel);form.AcceptButton=next;form.CancelButton=cancel;
        if(form.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return false;Core.LicenseAgreement.Accept(path,language);return true;
    }
}
