using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SysAdminAssistant.Desktop;

public sealed record Choice(object? Key, string Label) { public override string ToString() => Key is int or long or Guid ? Label : SysAdminAssistant.Core.ProductText.Get(Label); }
public sealed class FormDialog : DialogWindow
{
    private readonly StackPanel fields = new();
    private StackPanel currentFields;
    private StackPanel sectionHost;
    private TabControl? tabs;
    private bool actionPending, dirty, loaded, viewOnly, customSubmit;
    private readonly Dictionary<string, StackPanel> tabFields = new();
    private readonly Button save = new() { Content = "Сохранить", IsDefault = true };
    private readonly Button cancel = new() { Content = "Отмена", IsCancel = true };
    private readonly Dictionary<string, FrameworkElement> controls = new();
    private readonly Dictionary<string, CheckBox[]> dayControls = new();
    private readonly TextBlock error = new() { Foreground = DesktopTheme.Brush("#B42335"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) };
    public Func<Task<bool>>? SaveAsync { get; set; }
    public Func<string?>? Validate { get; set; }
    public FormDialog(Window owner, string title, double width = 540)
    {
        HasOwnHeading=true;
        currentFields = sectionHost = fields;
        Owner = owner; Title = title; Width = Math.Min(width, SystemParameters.WorkArea.Width - 40); MaxHeight = SystemParameters.WorkArea.Height - 40; MinWidth=Math.Min(420,Width); ResizeMode=ResizeMode.CanResizeWithGrip;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new(18), LastChildFill = true };
        var header=new StackPanel{Margin=new(0,0,0,18)};header.Children.Add(new TextBlock{Text=title,FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});header.Children.Add(new TextBlock{Text="Параметры и сведения",FontSize=12,Foreground=DesktopTheme.Brush("#60738F"),Margin=new(0,5,0,0)});DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); footer.Children.Add(StatusViewport(error,56));
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        save.Style = (Style)FindResource("PrimaryButton");
        save.Click += async (_, _) => {
            if(actionPending)return;
            error.Text="";
            try{
                var message=Validate?.Invoke();
                if(message!=null){error.Text=message;return;}
                if(SaveAsync!=null){
                    actionPending=true;fields.IsEnabled=false;save.IsEnabled=false;cancel.IsEnabled=false;
                    try{if(!await SaveAsync())return;}
                    finally{actionPending=false;fields.IsEnabled=true;save.IsEnabled=true;cancel.IsEnabled=true;}
                }
                DialogResult=true;
            }catch(Exception e){error.Text=e.Message;}
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); footer.Children.Add(buttons); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = fields, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        Loaded += (_, _) => {
            loaded=true;
            controls.Values.FirstOrDefault(c => c.IsVisible && c.IsEnabled)?.Focus();
            void Changed() {if(loaded && !viewOnly)dirty=true;}
            foreach(var control in controls.Values) {
                if(control is TextBox text && !text.IsReadOnly)text.TextChanged+=(_,_)=>Changed();
                if(control is PasswordBox password)password.PasswordChanged+=(_,_)=>Changed();
                if(control is ComboBox combo)combo.SelectionChanged+=(_,_)=>Changed();
                if(control is ComboBox editable && editable.IsEditable)editable.AddHandler(TextBox.TextChangedEvent,new TextChangedEventHandler((_,_)=>Changed()));
                if(control is CheckBox check){check.Checked+=(_,_)=>Changed();check.Unchecked+=(_,_)=>Changed();}
            }
            foreach(var check in dayControls.Values.SelectMany(x=>x)){check.Checked+=(_,_)=>Changed();check.Unchecked+=(_,_)=>Changed();}
        };
        Closing += (_, e) => {
            if(actionPending){e.Cancel=true;return;}
            if(dirty && !viewOnly && DialogResult!=true && !customSubmit &&
               AppMessageBox.Show(this,"Изменения не сохранены. Закрыть форму и отменить изменения?","Несохранённые изменения",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)e.Cancel=true;
        };
    }
    public FormDialog Action(string label, Func<Task<string>> action)
    {
        var button=new Button { Content=label,Margin=new(0,8,0,8) };PlaceAction(button);
        button.Click+=async (_,_)=>{
            if(actionPending)return;actionPending=true;button.IsEnabled=false;save.IsEnabled=false;error.Text="Проверка…";
            try {error.Text=await action();}catch(Exception e){error.Text=e.Message;}
            finally{actionPending=false;button.IsEnabled=true;save.IsEnabled=true;}
        };return this;
    }
    public FormDialog Tab(string title)
    {
        if (tabs == null)
        {
            SizeToContent=SizeToContent.Manual;
            Height=Math.Min(660,MaxHeight);
            tabs = new TabControl { Margin = new(0, 0, 0, 12) };
            fields.Children.Add(tabs);
        }
        if (!tabFields.TryGetValue(title, out var panel))
        {
            panel = new StackPanel { Margin = new(2, 8, 2, 0) }; tabFields.Add(title, panel); tabs.Items.Add(new TabItem { Header = title, Content = panel });
        }
        currentFields = sectionHost = panel; return this;
    }
    public FormDialog TabVisible(string title,bool visible){
        if(tabs==null)return this;
        foreach(TabItem tab in tabs.Items)if(Equals(tab.Header,title)||Equals(tab.Header,SysAdminAssistant.Core.ProductText.Get(title)))tab.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        if(tabs.SelectedItem is TabItem selected&&selected.Visibility!=Visibility.Visible)tabs.SelectedItem=tabs.Items.Cast<TabItem>().FirstOrDefault(t=>t.Visibility==Visibility.Visible);
        return this;
    }
    public FormDialog SelectionChanged(string key,Action action){((ComboBox)controls[key]).SelectionChanged+=(_,_)=>action();return this;}
    public void Choices(string key,IEnumerable<Choice> choices,object? selected){var c=(ComboBox)controls[key];var rows=choices.ToArray();c.ItemsSource=rows;c.SelectedItem=rows.FirstOrDefault(x=>Equals(x.Key,selected));}
    public FormDialog FieldHeight(string key,double height){controls[key].Height=height;return this;}
    public void FieldEnabled(string key,bool value)=>controls[key].IsEnabled=value;
    public void MarkDirty()=>dirty=true;
    public FormDialog TextPreview(string text)
    {
        if(Content is not DockPanel root)return this;var editor=root.Children[root.Children.Count-1];root.Children.Remove(editor);var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var preview=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,0,16,0),MinHeight=360};grid.Children.Add(preview);Grid.SetColumn(editor,1);grid.Children.Add(editor);root.Children.Add(grid);SizeToContent=SizeToContent.Manual;Height=Math.Min(780,SystemParameters.WorkArea.Height-50);return this;
    }
    public FormDialog Submit(string label, bool danger = false) { customSubmit=true; save.Content = label; save.Style = (Style)FindResource(danger ? "DangerButton" : "PrimaryButton"); return this; }
    public FormDialog ViewOnly() { viewOnly=true; save.Visibility = Visibility.Collapsed; save.IsDefault = false; cancel.Content = "Закрыть"; return this; }
    public FormDialog Text(string key, string label, string value = "", bool multiline = false, bool readOnly = false)
    {
        if(readOnly && key is "details" or "result" or "status" or "advice")value=SysAdminAssistant.Core.ProductText.Get(value);
        Label(label); var box = new TextBox { Text = value, IsReadOnly = readOnly, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = multiline ? 125 : double.NaN, VerticalContentAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled };
        LanguageUi.Preserve(box);
        controls[key] = box; currentFields.Children.Add(box); return this;
    }
    internal void InsertWorkspacePreview(UIElement preview)=>currentFields.Children.Add(preview);
    public FormDialog Section(string title)
    {
        var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=title,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12),Foreground=DesktopTheme.Brush("#1E375A")});
        sectionHost.Children.Add(new Border{Child=panel,Background=DesktopTheme.Brush("#F7FAFE"),BorderBrush=DesktopTheme.Brush("#E1E9F4"),BorderThickness=new(1),CornerRadius=new(10),Padding=new(16),Margin=new(0,0,0,12)});currentFields=panel;return this;
    }
    public FormDialog Fact(string label,string value)
    {
        currentFields.Children.Add(new TextBlock{Text=label,FontSize=12,Foreground=DesktopTheme.Brush("#60738F"),Margin=new(0,0,0,3)});
        currentFields.Children.Add(new TextBox{Text=value,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,Height=double.NaN,MinHeight=0,MaxHeight=150,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.Transparent,BorderThickness=new(0),Padding=new(0),Margin=new(0,0,0,12),FontSize=14});return this;
    }
    private void PlaceAction(Button button)
    {
        if(currentFields.Children.Count>0&&currentFields.Children[currentFields.Children.Count-1] is WrapPanel existing&&Equals(existing.Tag,"form-actions"))existing.Children.Add(button);
        else{var row=new WrapPanel{Tag="form-actions",Margin=new(0,4,0,4)};row.Children.Add(button);currentFields.Children.Add(row);}
    }
    public FormDialog TextHeight(string key, double height) { controls[key].Height = height; return this; }
    public FormDialog Secret(string key, string label) { Label(label); var box = new PasswordBox(); controls[key] = box; currentFields.Children.Add(box); return this; }
    public FormDialog Select(string key, string label, IEnumerable<Choice> choices, object? selected)
    {
        Label(label); var values = choices.ToArray(); var box = new ComboBox { ItemsSource = values, SelectedItem = values.FirstOrDefault(x => Equals(x.Key, selected)), IsEditable = false };
        controls[key] = box; currentFields.Children.Add(box); return this;
    }
    public FormDialog Suggest(string key, string label, IEnumerable<string> values, string value = "")
    {
        Label(label); var box = new ComboBox { IsEditable = true, ItemsSource = values.ToArray(), Text = value, IsTextSearchEnabled = true };
        LanguageUi.Preserve(box); controls[key] = box; currentFields.Children.Add(box); return this;
    }
    public void ToggleChanged(string key,Action change){var c=(CheckBox)controls[key];c.Checked+=(_,_)=>change();c.Unchecked+=(_,_)=>change();}
    public void SetChecked(string key,bool value)=>((CheckBox)controls[key]).IsChecked=value;
    public FormDialog Toggle(string key, string label, bool value) { var c = new CheckBox { Content = label, IsChecked = value }; controls[key] = c; currentFields.Children.Add(c); return this; }
    public FormDialog Days(string key, int mask, string label = "Дни ожидаемого завершения")
    {
        Label(label); var panel = new WrapPanel(); var days = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var boxes = days.Select((d, i) => new CheckBox { Content = d, IsChecked = (mask & (1 << i)) != 0 }).ToArray();
        foreach (var box in boxes) panel.Children.Add(box); dayControls[key] = boxes; currentFields.Children.Add(panel); return this;
    }
    public FormDialog Element(UIElement element) { currentFields.Children.Add(element); return this; }
    public FormDialog Note(string text) { currentFields.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = DesktopTheme.Brush("#64748B"), Margin = new(0, 0, 0, 14) }); return this; }
    public FormDialog Action(string label, Action action) { var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => { try { action(); } catch (Exception e) { error.Text = e.Message; } }; PlaceAction(button); return this; }
    public FormDialog ActionAsync(string label, Func<Task> action)
    {
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) =>
        {
            if (actionPending) return;
            actionPending = true;
            fields.IsEnabled = false; save.IsEnabled = false; cancel.IsEnabled = false;
            error.Text = "";
            try { await action(); }
            catch (Exception e) { error.Text = e.Message; }
            finally
            {
                actionPending = false;
                fields.IsEnabled = true; save.IsEnabled = true; cancel.IsEnabled = true;
            }
        };
        PlaceAction(button);
        return this;
    }
    public FormDialog ExpireAfter(TimeSpan duration)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); foreach (var c in controls.Values) { if (c is TextBox text) text.Clear(); if (c is PasswordBox secret) secret.Clear(); } DialogResult = false; };
        Loaded += (_, _) => timer.Start(); Closed += (_, _) => timer.Stop(); return this;
    }
    private void Label(string label) => currentFields.Children.Add(new TextBlock { Text = label, Margin = new(0, 4, 0, 6), FontWeight = FontWeights.SemiBold });
    public string Value(string key) => controls[key] switch { TextBox b => b.Text.Trim(), PasswordBox b => b.Password, ComboBox b => b.Text.Trim(), _ => "" };
    public bool HasSelection(string key) => (controls[key] as ComboBox)?.SelectedItem is Choice;
    public object? Selected(string key) => (controls[key] as ComboBox)?.SelectedItem is Choice choice ? choice.Key : null;
    public T Selected<T>(string key) => Selected(key) is T value ? value : throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Выберите значение из списка"));
    public bool Checked(string key) => ((CheckBox)controls[key]).IsChecked == true;
    public int Mask(string key) => dayControls[key].Select((d, i) => d.IsChecked == true ? 1 << i : 0).Sum();
    public int Number(string key, int min, int max)
    {
        if (!int.TryParse(Value(key), out int n) || n < min || n > max) throw new InvalidOperationException($"Число должно быть от {min} до {max}"); return n;
    }
    public DateTime Date(string key) => DateTime.TryParseExact(Value(key), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Дата должна быть ДД.ММ.ГГГГ"));
    public int Time(string key) => TimeSpan.TryParseExact(Value(key), @"hh\:mm", CultureInfo.InvariantCulture, out var t) && t.TotalMinutes < 1440 ? (int)t.TotalMinutes : throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Время должно быть ЧЧ:ММ"));
    public string Selection(string key) => ((TextBox)controls[key]).SelectedText;
    public void SetEditableChoice(string key, string value) => ((ComboBox)controls[key]).Text = value;
    public void Set(string key, string value) => ((TextBox)controls[key]).Text = value;
}
