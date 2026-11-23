namespace SysAdminAssistant.Setup;
internal static class SetupLanguage
{
    internal static string? Choose(bool force=false)
    {
        var configured=System.Environment.GetEnvironmentVariable("SAA_LANGUAGE");
        if(!force&&configured is "ru" or "en")return configured;
        var preference=System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SysAdminAssistantSetup","language.txt");
        if(configured is not ("ru" or "en"))try{var saved=System.IO.File.ReadAllText(preference).Trim();if(saved is "ru" or "en")configured=saved;}catch(System.IO.IOException){}catch(System.UnauthorizedAccessException){}
        using var form=new System.Windows.Forms.Form{Text="SysAdmin Assistant · Language / Язык",ClientSize=new System.Drawing.Size(420,155),StartPosition=System.Windows.Forms.FormStartPosition.CenterScreen,FormBorderStyle=System.Windows.Forms.FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=new System.Drawing.Font("Segoe UI",10)};
        var label=new System.Windows.Forms.Label{Text="Выберите язык / Select language",AutoSize=true,Location=new(20,18)};
        var languages=new System.Windows.Forms.ComboBox{DropDownStyle=System.Windows.Forms.ComboBoxStyle.DropDownList,Location=new(20,48),Width=380};
        languages.Items.AddRange(["Русский","English"]);languages.SelectedIndex=(configured??(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="ru"?"ru":"en"))=="ru"?0:1;
        var next=new System.Windows.Forms.Button{Text="Продолжить / Continue",DialogResult=System.Windows.Forms.DialogResult.OK,Location=new(205,100),Width=195,Height=35};
        form.Controls.AddRange([label,languages,next]);form.AcceptButton=next;
        if(form.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return null;
        var selected=languages.SelectedIndex==0?"ru":"en";
        System.Environment.SetEnvironmentVariable("SAA_LANGUAGE",selected);
        try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(preference)!);System.IO.File.WriteAllText(preference,selected);}catch(System.IO.IOException){}catch(System.UnauthorizedAccessException){}
        return selected;
    }
}
