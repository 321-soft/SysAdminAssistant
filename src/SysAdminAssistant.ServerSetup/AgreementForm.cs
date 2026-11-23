using SysAdminAssistant.Core;

namespace SysAdminAssistant.ServerSetup;

internal sealed class AgreementForm : Form
{
    private static string RecordPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysAdminAssistantServerSetup", "legal", "acceptance.json");
    public static bool EnsureAccepted() { if(Environment.GetEnvironmentVariable("SAA_EULA_ACCEPTED")==LicenseAgreement.Hash){LicenseAgreement.Accept(RecordPath,Environment.GetEnvironmentVariable("SAA_LANGUAGE")=="en"?"EN":"RU");return true;} if (LicenseAgreement.IsAccepted(RecordPath)) return true; using var form = new AgreementForm(); return form.ShowDialog() == DialogResult.OK; }
    private static string DisplayText(string language) => LicenseAgreement.CanonicalText(LicenseAgreement.Text(language)).Replace("\n", "\r\n");
    private AgreementForm()
    {
        Text = "Лицензионное соглашение / License Agreement"; Width = 800; Height = 700;
        MinimumSize = new Size(480,400); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top }; language.Items.AddRange(new object[]{"Русский","English"}); language.SelectedIndex = ProductText.Language=="en"?1:0;
        var text = new TextBox { Text = DisplayText(ProductText.Language), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill };
        var checkbox = new CheckBox { Text = "Я принимаю условия / I accept the terms", AutoSize = true, Margin = new Padding(0,12,0,12) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Отмена / Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var accept = new Button { Text = "Продолжить / Continue", AutoSize = true, Enabled = false };
        var save = new Button { AutoSize = true };
        string Label(string ru,string en)=>language.SelectedIndex==1?en:ru;
        void RefreshLabels(){
            Text=Label("Лицензионное соглашение","License Agreement");
            checkbox.Text=Label("Я принимаю условия лицензионного соглашения","I accept the license terms");
            accept.Text=Label("Продолжить","Continue");
            cancel.Text=Label("Отмена","Cancel");
            save.Text=Label("Сохранить текст…","Save text…");
        }
        save.Click+=(_,_)=>{
            using var dialog=new SaveFileDialog{FileName="SysAdmin_Assistant_License_"+Label("RU","EN")+".txt",Filter=Label("Текстовые файлы (*.txt)|*.txt","Text files (*.txt)|*.txt")};
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            try{File.WriteAllText(dialog.FileName,text.Text);}catch(Exception error){SetupMessageBox.Show(error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);}
        };
        RefreshLabels();
        checkbox.CheckedChanged += (_,_) => accept.Enabled = checkbox.Checked;
        language.SelectedIndexChanged += (_,_) => { text.Text = DisplayText(language.SelectedIndex == 1 ? "EN" : "RU"); text.Select(0,0);text.ScrollToCaret();RefreshLabels();checkbox.Checked = false; };
        accept.Click += (_,_) => { try { ProductText.DefaultLanguage=language.SelectedIndex==1?"en":"ru";LicenseAgreement.Accept(RecordPath,language.SelectedIndex == 1 ? "EN" : "RU"); DialogResult = DialogResult.OK; } catch(Exception error) { SetupMessageBox.Show(error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error); } };
        buttons.Controls.Add(cancel); buttons.Controls.Add(accept); buttons.Controls.Add(save); layout.Controls.Add(language,0,0); layout.Controls.Add(text,0,1); layout.Controls.Add(checkbox,0,2); layout.Controls.Add(buttons,0,3); Controls.Add(layout); CancelButton = cancel; SetupTheme.Apply(this);
    }
}
