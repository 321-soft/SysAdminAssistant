using SysAdminAssistant.Core;

internal static class MailNumericSpecs
{
    public static void Run(Action<string,bool> check)
    {
        const string marker="Доступное пространство:";
        const string body="Емкость - Доступное пространство: 1893.98 ГБ (8.84%)";
        var info=new MailNumericCondition(marker,"%",false,">=",0,false);
        var warning=info with{Operator="<",Threshold=10};
        var critical=warning with{Threshold=9};
        check("Mail numeric rule extracts percentage or volume from the same line",
            warning.Evaluate(body).Match==8.84m && (warning with{Anchor="ГБ",Threshold=2000}).Evaluate(body).Match==1893.98m);
        check("Mail numeric rules accept decimal commas and signed numbers",
            warning.Evaluate(body.Replace("8.84","8,84")).Match==8.84m &&
            new MailNumericCondition("Temperature","Temperature:",true,"<=",0,false).Evaluate("Temperature: -2.5 °C").Match==-2.5m);
        check("Mail numeric rules reject ambiguous or missing numbers by default",
            warning.Evaluate(body+"\r\n"+body).Match==null && warning.Evaluate("Доступное пространство: 0 ГБ\n8%").Match==null &&
            warning.Evaluate("Доступное пространство: (нет данных%)").Match==null);
        check("Mail numeric any mode can select a qualifying value after other disks",
            (warning with{Any=true}).Evaluate(body.Replace("8.84","12.5")+"\n"+body).Match==8.84m);
        var rule=new MailSourceEvent("Ёмкость","отчёт","",true,Severity.Info,BackupResult.Event,""){Numeric=new([info],[warning],[critical])};
        var profile=new KeywordProfile{Provider="Хранилище",SubjectTerms="отчёт",EventRulesJson=MailSources.WriteEvents([rule])};
        var report=new MailReport("test","key","sender","отчёт",body,DateTime.Now);
        var analysis=ReportAnalyzer.Analyze(report,[profile]);
        check("Mail source numeric levels survive JSON and choose highest matching level",
            analysis.Severity==Severity.Critical && analysis.Evidence.Contains("8.84 < 9"));
        check("Mail source numeric levels honor existing text and do not use default when no number is found",
            MailSources.Match(profile,report with{Subject="другое"}) is {Length:0} &&
            MailSources.Match(profile,report with{Body="нет данных"}) is {Length:0});
        var legacy=rule with{Numeric=null};
        check("Mail source legacy rule still matches and omits numeric field",
            !MailSources.WriteEvents([legacy]).Contains("\"Numeric\"") &&
            MailSources.Match(new KeywordProfile{EventRulesJson=MailSources.WriteEvents([legacy])},report).Single().Severity==Severity.Info);
    }
}
