using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SysAdminAssistant.Core;

namespace SysAdminAssistant.Desktop;

internal sealed class AgreementWindow : DialogWindow
{
    private static string RecordPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysAdminAssistant", "legal", "acceptance.json");

    public static bool EnsureAccepted() => LicenseAgreement.IsAccepted(RecordPath) || new AgreementWindow(true).ShowDialog() == true;

    public AgreementWindow(bool requireAcceptance)
    {
        HasOwnHeading = true;
        Width = 800;
        Height = 700;
        MinWidth = 440;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new DockPanel { Margin = new Thickness(16) };
        var language = new ComboBox { ItemsSource = new[] { "Русский", "English" }, SelectedIndex = ProductText.Language == "en" ? 1 : 0, Margin = new Thickness(0,0,0,12) };
        DockPanel.SetDock(language, Dock.Top);
        root.Children.Add(language);

        var bottom = new StackPanel { Margin = new Thickness(0,12,0,0) };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        var checkbox = new CheckBox { Margin = new Thickness(0,0,0,10), Visibility = requireAcceptance ? Visibility.Visible : Visibility.Collapsed };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new Button { IsEnabled = false, Visibility = requireAcceptance ? Visibility.Visible : Visibility.Collapsed };
        var close = new Button { IsCancel = true };
        var save = new Button();
        buttons.Children.Add(save);
        buttons.Children.Add(accept);
        buttons.Children.Add(close);
        bottom.Children.Add(checkbox);
        bottom.Children.Add(buttons);

        var reader = new RichTextBox
        {
            IsReadOnly = true,
            IsDocumentEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0)
        };
        reader.SetResourceReference(BackgroundProperty,"SaaThemeFFFFFF");
        reader.SetResourceReference(ForegroundProperty,"SaaTheme17243C");
        root.Children.Add(reader);
        Content = root;

        string CurrentLanguage() => language.SelectedIndex == 1 ? "en" : "ru";
        string Label(string russian,string english) => CurrentLanguage() == "en" ? english : russian;
        void ShowAgreement()
        {
            Title = Label("Лицензионное соглашение", "License Agreement");
            checkbox.Content = Label("Я принимаю условия лицензионного соглашения", "I accept the license terms");
            accept.Content = Label("Продолжить", "Continue");
            close.Content = requireAcceptance ? Label("Отмена", "Cancel") : Label("Закрыть", "Close");
            save.Content = Label("Сохранить текст…", "Save text…");
            checkbox.IsChecked = false;
            reader.Document = CreateDocument(LicenseAgreement.Text(CurrentLanguage()));
            reader.ScrollToHome();
        }

        language.SelectionChanged += (_,_) => ShowAgreement();
        checkbox.Checked += (_,_) => accept.IsEnabled = true;
        checkbox.Unchecked += (_,_) => accept.IsEnabled = false;
        accept.Click += (_,_) =>
        {
            try
            {
                var selected = CurrentLanguage();
                LanguageUi.Save(selected);
                ProductText.DefaultLanguage = selected;
                LicenseAgreement.Accept(RecordPath, selected.ToUpperInvariant());
                DialogResult = true;
            }
            catch(Exception error) { AppMessageBox.Show(this,error.Message,Title); }
        };
        save.Click += (_,_) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "SysAdmin_Assistant_License_" + CurrentLanguage().ToUpperInvariant() + ".txt",
                Filter = Label("Текстовые файлы (*.txt)|*.txt", "Text files (*.txt)|*.txt")
            };
            if(dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, LicenseAgreement.Text(CurrentLanguage())); }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException) { AppMessageBox.Show(this,error.Message,Title); }
        };
        close.Click += (_,_) => Close();
        // The agreement language selector is independent from the rest of the UI.
        LanguageUi.Preserve(this);
        ShowAgreement();
    }

    private static FlowDocument CreateDocument(string text)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(10,4,14,4), FontSize = 15, LineHeight = 23 };
        var index = 0;
        foreach(var line in LicenseAgreement.CanonicalText(text).Split('\n'))
        {
            if(string.IsNullOrWhiteSpace(line)) continue;
            var period = line.IndexOf('.');
            var heading = period > 0 && period + 1 < line.Length && line[period + 1] == ' ' && int.TryParse(line.AsSpan(0,period),out _);
            var paragraph = new Paragraph(new Run(line)) { Margin = new Thickness(0,0,0,10) };
            if(index++ == 0 || heading)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.FontSize = heading ? 17 : 22;
                paragraph.LineHeight = heading ? 25 : 31;
                if(heading) paragraph.Margin = new Thickness(0,10,0,8);
            }
            doc.Blocks.Add(paragraph);
        }
        return doc;
    }
}
