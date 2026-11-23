using System.Drawing;
using System.Windows.Forms;
namespace SysAdminAssistant.ServerSetup;
internal static class SetupMessageBox
{
    public static DialogResult Show(string text,string caption="SysAdmin Assistant",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)=>Show(null,text,caption,buttons,icon,defaultButton);
    public static DialogResult Show(IWin32Window? owner,string text,string caption="SysAdmin Assistant",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)
    {
        text=SysAdminAssistant.Core.ProductText.Get(text);caption=SysAdminAssistant.Core.ProductText.Get(caption);
        var choices=buttons switch{MessageBoxButtons.OK=>new[]{DialogResult.OK},MessageBoxButtons.OKCancel=>new[]{DialogResult.OK,DialogResult.Cancel},MessageBoxButtons.YesNo=>new[]{DialogResult.Yes,DialogResult.No},MessageBoxButtons.YesNoCancel=>new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel},_=>throw new ArgumentOutOfRangeException(nameof(buttons))};
        using var form=new Form{Text=caption,ClientSize=new Size(620,330),MinimumSize=new Size(420,250),StartPosition=owner==null?FormStartPosition.CenterScreen:FormStartPosition.CenterParent,ShowInTaskbar=owner==null,MinimizeBox=false,MaximizeBox=false,AutoScaleMode=AutoScaleMode.Dpi};
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=3};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label{Text=caption,Font=new Font("Segoe UI",18,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,14)},0,0);
        root.Controls.Add(new TextBox{Text=text,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,BackColor=Color.White},0,1);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,12,0,0)};
        var defaultIndex=Math.Clamp((int)defaultButton/256,0,choices.Length-1);
        for(int i=choices.Length-1;i>=0;i--){var result=choices[i];var button=new Button{Text=result switch{DialogResult.Yes=>"Да",DialogResult.No=>"Нет",DialogResult.Cancel=>"Отмена",_=>"Понятно"},DialogResult=result,AutoSize=true,MinimumSize=new Size(90,36)};actions.Controls.Add(button);if(i==defaultIndex)form.AcceptButton=button;if(result==DialogResult.Cancel||result==DialogResult.No)form.CancelButton=button;}
        root.Controls.Add(actions,0,2);form.Controls.Add(root);SetupTheme.Apply(form);
        var selected=owner==null?form.ShowDialog():form.ShowDialog(owner);
        return selected==DialogResult.Cancel&&buttons==MessageBoxButtons.YesNo?DialogResult.No:selected==DialogResult.Cancel&&buttons==MessageBoxButtons.OK?DialogResult.OK:selected;
    }
}
