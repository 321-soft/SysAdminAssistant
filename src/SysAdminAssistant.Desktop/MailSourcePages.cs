using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Desktop;

public partial class MainWindow
{
    private async Task ShowMailSources()
    {
        AccessRules.Require(remote.CurrentUser,"reports",2);
        await using var db=factory.Create();
        var profiles=await db.Profiles.AsNoTracking().OrderBy(x=>x.Provider).ToArrayAsync(lifetime.Token);
        var sources=profiles.Where(x=>x.EventRulesJson.Length>0).ToArray();
        var pick=new FormDialog(this,"Источники почты",760).Submit("Открыть настройки")
            .Note("Источники общие для всех устройств. Они определяют, что произошло по содержимому письма. Привязка к устройству или заданию настраивается отдельно.")
            .Select("source","Пользовательский источник",sources.Select(x=>new Choice(x.Id,x.Provider)),sources.FirstOrDefault()?.Id);
        string operation="";
        void Open(string value){operation=value;pick.DialogResult=true;}
        pick.Action("Добавить источник",()=>Open("new"))
            .Action("Импорт JSON",()=>Open("import"))
            .Action("Скачать шаблон JSON",()=>SaveSourceFile(new(1,"MailSource","Пример источника","notifications@example.invalid","Отчёт службы","",true,[new("Служба завершилась с ошибкой","Завершено с ошибкой","",true,Severity.Warning,BackupResult.Event,"Проверьте журнал службы.")]),"mail-source-template.json"))
            .Action("Экспорт выбранного",()=>{if(pick.Selected("source") is int id)SaveSourceFile(MailSources.From(sources.Single(x=>x.Id==id)),"mail-source.json");else throw new InvalidOperationException("Выберите источник.");})
            .Action("Словари разбора почты",()=>Open("legacy"));
        pick.Validate=()=>operation==""&&!pick.HasSelection("source")?"Выберите источник или добавьте новый.":null;
        if(pick.ShowDialog()!=true)return;
        if(operation=="legacy"){await EditKeywords();return;}
        MailSourcePackage? package=null;
        if(operation=="import"){
            var file=new OpenFileDialog{Filter="Источник почты (*.json)|*.json"};
            if(file.ShowDialog(this)!=true)return;
            using var stream=File.OpenRead(file.FileName);
            if(stream.Length>256_000)throw new ArgumentException("Файл источника слишком большой.");
            using var reader=new StreamReader(stream);
            var chars=new char[100_001];var count=await reader.ReadBlockAsync(chars.AsMemory(),lifetime.Token);
            package=MailSources.Parse(new string(chars,0,count));
        }
        var current=operation==""&&pick.Selected("source") is int selected?sources.Single(x=>x.Id==selected):null;
        if(await EditMailSource(current,package,null))await ShowPage(page);
    }

    private void SaveSourceFile(MailSourcePackage package,string name)
    {
        var file=new SaveFileDialog{FileName=name,DefaultExt=".json",Filter="Источник почты (*.json)|*.json"};
        if(file.ShowDialog(this)==true)File.WriteAllText(file.FileName,MailSources.Export(package));
    }

    private async Task CreateMailSource(ReportEvent report)
    {
        AccessRules.Require(remote.CurrentUser,"reports",2);
        var sample=await remote.CommandAsync<MailView>("mail-view",new MailViewRequest(report.Id),lifetime.Token);
        if(!await EditMailSource(null,null,sample))return;
        if(AppMessageBox.Show(this,"Источник сохранён. Повторно проверить только это письмо?","Источники почты",MessageBoxButton.YesNo)==MessageBoxResult.Yes)await ReprocessMailSource(report.Id);
        else await ShowPage(11);
    }

    private async Task ReprocessMailSource(int id)
    {
        AccessRules.Require(remote.CurrentUser,"reports",2);
        var result=await remote.CommandAsync<MailRecheckResult>("mail-recheck",new MailViewRequest(id),lifetime.Token);
        if(!result.Applied){AppMessageBox.Show(this,ProductText.Get(result.Issue),"Проверка письма");return;}
        await remote.RefreshAsync(lifetime.Token);
        await ShowPage(11);
        var summary=ProductText.Get(result.IsPending?"Письмо требует проверки.":"Письмо разобрано по текущим правилам.");
        AppMessageBox.Show(this,summary+"\n"+ProductText.Get("Почтовый профиль")+": "+ProductText.Get(result.Provider)
            +(result.IsPending?"\n"+ProductText.Get(result.Issue):""),"Проверка письма");
    }

    private async Task<bool> EditMailSource(KeywordProfile? existing,MailSourcePackage? imported,MailView? sample)
    {
        await using var db=factory.Create();
        var profiles=await db.Profiles.AsNoTracking().ToArrayAsync(lifetime.Token);
        var p=imported??(existing!=null?MailSources.From(existing):new(1,"MailSource","",sample?.Sender??"",sample?.Subject??"","",true,[]));
        var events=p.Events.ToList();
        var form=new FormDialog(this,"Настройки источника почты",800)
            .Note("Для каждого поля укажите одну или несколько устойчивых фраз через точку с запятой. Не включайте даты, номера и другие переменные части.")
            .Text("name","Название источника",p.Name,false,existing!=null)
            .Text("sender","Отправитель содержит",p.Sender)
            .Text("subject","Тема содержит",p.Subject)
            .Text("body","Текст содержит",p.Body,true)
            .Toggle("all","Должны совпасть все заполненные поля",p.MatchAll);
        form.Tab("События").Note("Добавьте условия для каждого события. Если письмо подходит нескольким событиям, оно останется на проверке.")
            .Select("event","Событие",[],null);
        void ListEvents(int index)=>form.Choices("event",events.Select((e,i)=>new Choice(i,e.Title)),events.Count==0?null:Math.Clamp(index,0,events.Count-1));
        ListEvents(0);
        form.Action("Добавить событие",()=>{var value=EditMailSourceEvent(form,null,form.Value("testBody"));if(value==null)return;events.Add(value);ListEvents(events.Count-1);form.MarkDirty();})
            .Action("Изменить выбранное",()=>{var i=form.Selected<int>("event");var value=EditMailSourceEvent(form,events[i],form.Value("testBody"));if(value==null)return;events[i]=value;ListEvents(i);form.MarkDirty();})
            .Action("Удалить выбранное",()=>{var i=form.Selected<int>("event");events.RemoveAt(i);ListEvents(i);form.MarkDirty();});
        form.Tab("Проверка").Note("Тест не записывает письмо, не меняет отчёты и не отправляет уведомления.")
            .Text("testSender","Отправитель",sample?.Sender??"").Text("testSubject","Тема",sample?.Subject??"")
            .Text("testBody","Текст письма",sample?.Body??"",true).TextHeight("testBody",155)
            .Text("result","Результат проверки","",true,true).TextHeight("result",115);
        KeywordProfile Build()
        {
            var input=new MailSourcePackage(1,"MailSource",form.Value("name"),form.Value("sender"),form.Value("subject"),form.Value("body"),form.Checked("all"),events.ToArray());
            MailSources.Validate(input);
            if(profiles.Any(x=>x.Id!=existing?.Id&&x.Provider.Equals(input.Name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Имя источника уже используется. Выберите другое имя или откройте существующий источник.");
            var profile=KeywordDefaults.For(input.Name);
            if(existing!=null)db.Entry(profile).CurrentValues.SetValues(existing);
            profile.SenderTerms=input.Sender;profile.SubjectTerms=input.Subject;profile.BodyTerms=input.Body;profile.MatchAllSourceFields=input.MatchAll;
            profile.EventRulesJson=MailSources.WriteEvents(input.Events);MailSources.ValidateProfile(profile);
            return profile;
        }
        form.Validate=()=>{try{Build();return null;}catch(ArgumentException e){return e.Message;}};
        form.Action("Проверить письмо",()=>{
            var candidate=Build();
            var result=ReportAnalyzer.Analyze(new("test","test",form.Value("testSender"),form.Value("testSubject"),form.Value("testBody"),AppTime.Now),profiles.Where(x=>x.Id!=existing?.Id).Append(candidate).ToArray());
            form.Set("result",$"Почтовый профиль: {result.Provider}\nСобытие: {result.Task}\nУровень: {result.Severity} · {Display.Result(result.Result)}\n{result.Evidence}\n{result.Details}");
        });
        if(form.ShowDialog()!=true)return false;
        var saved=Build();if(existing==null)db.Profiles.Add(saved);else db.Profiles.Update(saved);
        await db.SaveChangesAsync(lifetime.Token);
        await remote.RefreshAsync(lifetime.Token);
        return true;
    }

    private static Func<string> AddIndependentRulesTab(FormDialog form,string stored)
    {
        var rules=MailSources.ReadEvents(stored).ToList();
        form.Tab("Отдельные правила")
            .Note("У каждого правила свои условия, уровень и результат. Если ни одно правило не подходит, применяются настройки профиля.")
            .Select("independentRule","Правило",[],null);
        void Refresh(int index)=>form.Choices("independentRule",rules.Select((rule,i)=>new Choice(i,rule.Title)),rules.Count==0?null:Math.Clamp(index,0,rules.Count-1));
        Refresh(0);
        form.Action("Добавить отдельное правило",()=>{var rule=EditMailSourceEvent(form,null);if(rule==null)return;MailSources.WriteEvents([..rules,rule]);rules.Add(rule);Refresh(rules.Count-1);form.MarkDirty();})
            .Action("Изменить правило",()=>{if(form.Selected("independentRule") is not int i)return;var rule=EditMailSourceEvent(form,rules[i]);if(rule==null)return;MailSources.WriteEvents(rules.Select((x,j)=>i==j?rule:x).ToArray());rules[i]=rule;Refresh(i);form.MarkDirty();})
            .Action("Удалить правило",()=>{if(form.Selected("independentRule") is not int i)return;rules.RemoveAt(i);Refresh(i);form.MarkDirty();});
        return ()=>rules.Count==0?"":MailSources.WriteEvents(rules.ToArray());
    }

    private static MailSourceEvent? EditMailSourceEvent(Window owner,MailSourceEvent? e,string sampleBody="")
    {
        var form=new FormDialog(owner,"Правило события",820)
            .Tab("Условия события").Text("title","Название",e?.Title??"")
            .Text("subject","Тема содержит",e?.Subject??"")
            .Text("body","Текст содержит",e?.Body??"",true)
            .Toggle("all","Должны совпасть все заполненные поля",e?.MatchAll??true)
            .Select("severity","Уровень без числовых условий",[new Choice(Severity.Info,"Информация"),new Choice(Severity.Warning,"Предупреждение"),new Choice(Severity.Critical,"Критическое")],e?.Severity??Severity.Info)
            .Select("result","Результат",[new Choice(BackupResult.Event,"Обычное событие"),new Choice(BackupResult.Success,"Бэкап успешен"),new Choice(BackupResult.Warning,"Бэкап с предупреждением"),new Choice(BackupResult.Failed,"Ошибка бэкапа")],e?.Result??BackupResult.Event)
            .Text("advice","Что проверить или как исправить",e?.Advice??"",true);
        form.Tab("Числовые условия").Note("Добавьте условия в нужный уровень. Достаточно одного совпадения внутри уровня. Если сработало несколько уровней, используется самый высокий. Заполненные текстовые условия тоже должны выполняться.");
        var info=AddNumericLevel(form,"Информация",e?.Numeric?.Info);
        var warning=AddNumericLevel(form,"Предупреждение",e?.Numeric?.Warning);
        var critical=AddNumericLevel(form,"Критическое",e?.Numeric?.Critical);
        MailNumericLevels? ReadNumeric()
        {
            var result=new MailNumericLevels(info(),warning(),critical());
            return result.HasConditions?result:null;
        }
        form.Tab("Проверка числа")
            .Note("Вставьте текст письма или нужные строки. Проверка показывает результат каждого числового условия; условия темы и источника проверяются на вкладке «Проверка» источника.")
            .Text("numericSample","Текст письма",sampleBody,true)
            .Text("numericPreview","Результат проверки","",true,true).TextHeight("numericPreview",155)
            .Action("Проверить числа",()=>{
                var levels=ReadNumeric();
                var sample=form.Value("numericSample");
                if(sample.Length>250_000)throw new ArgumentException("Текст для проверки слишком большой.");
                var lines=levels?.Groups().SelectMany(g=>g.Conditions.Select(c=>NumericLevel(g.Level)+" · "+c.Line+": "+ProductText.Get(c.Evaluate(sample).Evidence))).ToArray();
                form.Set("numericPreview",lines is {Length:>0}?string.Join("\n",lines):"Добавьте хотя бы одно числовое условие.");
            });
        MailSourceEvent Read()=>new(form.Value("title"),form.Value("subject"),form.Value("body"),form.Checked("all"),form.Selected<Severity>("severity"),form.Selected<BackupResult>("result"),form.Value("advice")){Numeric=ReadNumeric()};
        form.Validate=()=>{try{MailSources.ValidateEvents([Read()]);return null;}catch(ArgumentException ex){return ex.Message;}};
        return form.ShowDialog()==true?Read():null;
    }
    private static string NumericLevel(Severity level)=>ProductText.Get(level switch
    {
        Severity.Critical=>"Критическое",Severity.Warning=>"Предупреждение",_=>"Информация"
    });

    private static Func<MailNumericCondition[]> AddNumericLevel(FormDialog form,string title,MailNumericCondition[]? conditions)
    {
        var list=new System.Windows.Controls.StackPanel();
        var rows=new List<Func<MailNumericCondition>>();
        form.Section(title).Element(list);
        var add=new System.Windows.Controls.Button{Content="+",Width=42,HorizontalAlignment=HorizontalAlignment.Left,ToolTip="Добавить числовое условие"};
        System.Windows.Automation.AutomationProperties.SetName(add,ProductText.Get("Добавить числовое условие")+" · "+ProductText.Get(title));
        void Add(MailNumericCondition? value)
        {
            if(rows.Count>=12)return;
            var panel=new System.Windows.Controls.StackPanel{Margin=new Thickness(0,0,0,10)};
            var marker=TextField("Строка содержит",value?.Line??"");
            var anchor=TextField("Текст рядом с числом",value?.Anchor??"");
            var threshold=TextField("Порог",value?.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)??"");
            var side=new System.Windows.Controls.ComboBox{ItemsSource=new[]{ProductText.Get("Число перед текстом"),ProductText.Get("Число после текста")},SelectedIndex=value?.After==true?1:0};
            var op=new System.Windows.Controls.ComboBox{ItemsSource=new[]{"<","≤",">","≥","="},SelectedItem=value?.Operator switch{"<="=>"≤",">="=>"≥",null=>"<",var symbol=>symbol}};
            var any=new System.Windows.Controls.CheckBox{Content="Любое совпадение",IsChecked=value?.Any??false,ToolTip="Если найдено несколько чисел, достаточно одного, прошедшего сравнение. Без флажка требуется ровно одно число."};
            var remove=new System.Windows.Controls.Button{Content="Удалить условие",HorizontalAlignment=HorizontalAlignment.Left};
            var input=new System.Windows.Controls.Grid();
            for(var i=0;i<5;i++)input.ColumnDefinitions.Add(new(){Width=i switch{0 or 2=>new GridLength(2,GridUnitType.Star),1=>new GridLength(1.7,GridUnitType.Star),3=>new GridLength(62),_=>new GridLength(1,GridUnitType.Star)}});
            void Field(int column,string label,FrameworkElement field)
            {
                var box=new System.Windows.Controls.StackPanel{Margin=new Thickness(0,0,8,0)};
                box.Children.Add(new System.Windows.Controls.TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,MinHeight=42});
                box.Children.Add(field);
                System.Windows.Automation.AutomationProperties.SetName(field,ProductText.Get(label));
                System.Windows.Controls.Grid.SetColumn(box,column);input.Children.Add(box);
            }
            Field(0,"Строка содержит",marker);Field(1,"Расположение числа",side);
            Field(2,"Текст рядом с числом",anchor);Field(3,"Знак",op);Field(4,"Порог",threshold);
            panel.Children.Add(input);
            var actions=new System.Windows.Controls.WrapPanel();actions.Children.Add(any);actions.Children.Add(remove);panel.Children.Add(actions);
            MailNumericCondition Read()
            {
                if(!MailNumericCondition.TryNumber(threshold.Text,out var n))throw new ArgumentException("Введите числовой порог, например 8,84.");
                var condition=new MailNumericCondition(marker.Text.Trim(),anchor.Text.Trim(),side.SelectedIndex==1,(string?)op.SelectedItem switch{"≤"=>"<=","≥"=>">=",var symbol=>symbol??""},n,any.IsChecked==true);
                condition.Validate();
                return condition;
            }
            rows.Add(Read);list.Children.Add(panel);add.IsEnabled=rows.Count<12;
            foreach(var field in new[]{marker,anchor,threshold})field.TextChanged+=(_,_)=>form.MarkDirty();
            side.SelectionChanged+=(_,_)=>form.MarkDirty();op.SelectionChanged+=(_,_)=>form.MarkDirty();
            any.Checked+=(_,_)=>form.MarkDirty();any.Unchecked+=(_,_)=>form.MarkDirty();
            remove.Click+=(_,_)=>{rows.Remove(Read);list.Children.Remove(panel);add.IsEnabled=true;form.MarkDirty();};
        }
        foreach(var condition in conditions??[])Add(condition);
        form.Element(add);
        add.Click+=(_,_)=>{Add(null);form.MarkDirty();};
        return ()=>rows.Select(read=>read()).ToArray();

        static System.Windows.Controls.TextBox TextField(string label,string value)=>new(){Text=value,ToolTip=label};
    }

}
