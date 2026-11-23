using System.Drawing;
using System.Windows.Forms;
namespace SysAdminAssistant.ServerSetup;
internal static class SetupTheme
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> Watching=new();
    public static void Apply(Form window)
    {
        window.Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        window.Text=SysAdminAssistant.Core.ProductText.Get(window.Text);
        window.BackColor=Color.FromArgb(245,248,254);window.ForeColor=Color.FromArgb(23,36,60);window.Font=new Font("Segoe UI",10);
        void Visit(Control parent){foreach(Control c in parent.Controls){
            if(c is not TextBoxBase){c.Text=SysAdminAssistant.Core.ProductText.Get(c.Text);if(!Watching.TryGetValue(c,out _)){Watching.Add(c,new object());c.TextChanged+=(_,_)=>{var translated=SysAdminAssistant.Core.ProductText.Get(c.Text);if(c.Text!=translated)c.Text=translated;};}}
            if(c is ComboBox combo){for(int index=0;index<combo.Items.Count;index++)if(combo.Items[index] is string item)combo.Items[index]=SysAdminAssistant.Core.ProductText.Get(item);}
            if(c is Button b){b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(205,216,231);b.FlatAppearance.MouseOverBackColor=Color.FromArgb(228,237,252);b.BackColor=Color.White;b.ForeColor=window.ForeColor;b.Padding=new Padding(8,3,8,3);b.MinimumSize=new Size(0,36);b.AutoSize=true;}
            else if(c is TextBoxBase t){t.BackColor=t.ReadOnly?Color.FromArgb(246,249,254):Color.White;t.ForeColor=window.ForeColor;}
            else if(c is TabPage tab)tab.BackColor=Color.White;
            else if(c is Label or CheckBox or RadioButton)c.ForeColor=window.ForeColor;
            if(c is FlowLayoutPanel f&&f.Controls.OfType<Button>().Any()){f.WrapContents=true;f.AutoSize=true;f.AutoSizeMode=AutoSizeMode.GrowAndShrink;if(f.Parent is TableLayoutPanel table){var row=table.GetRow(f);if(row>=0&&row<table.RowStyles.Count)table.RowStyles[row].SizeType=SizeType.AutoSize;}}
            Visit(c);
        }}Visit(window);
    }
}
