using System.Text.Json;
using SysAdminAssistant.Core;
internal static class AgentServiceProfileSpecs
{
    static AgentPolicy Policy(params string[] roles)=>new(){StandardServices=false,RoleProfilesConfigured=true,ServiceRoles=roles};
    static bool Has(AgentPolicy p,string n)=>p.Services.Any(s=>s.Name.Equals(n,StringComparison.OrdinalIgnoreCase));
    public static void Run(Action<string,bool> check)
    {
        AgentService[] observed=[new("NTDS","AD","Running","Automatic"),new("DNS","DNS","Stopped","Automatic"),new("DFSR","DFS","Stopped","Automatic"),new("NtFrs","Old FRS","Stopped","Disabled"),new("ADWS","AD WS","Running","Automatic")];
        var p=Policy("ad");AgentServiceProfiles.Sync(p,observed);
        check("AD profile adds directory authentication SYSVOL time and optional installed DNS",new[]{"NTDS","Kdc","Netlogon","LanmanServer","RpcSs","SamSs","EventSystem","LanmanWorkstation","Dnscache","W32Time","DNS","DFSR","ADWS"}.All(n=>Has(p,n)));
        check("AD ignores disabled legacy FRS",!Has(p,"NtFrs"));
        check("AD time warning and directory critical",p.Services.Single(s=>s.Name=="W32Time").Severity==Severity.Warning&&p.Services.Single(s=>s.Name=="NTDS").Severity==Severity.Critical);
        var q=Policy("ad");AgentServiceProfiles.Sync(q,[]);check("AD profile does not invent DNS on DC without DNS role",!Has(q,"DNS")&&Has(q,"NTDS"));
        check("Reconcile identical inventory is stable",!AgentServiceProfiles.Sync(p,observed));
        AgentServiceProfiles.Sync(p,[]);check("Disappeared monitored service stays expected",Has(p,"DNS")&&Has(p,"DFSR"));
        p.ServiceRoles=[];AgentServiceProfiles.Sync(p,observed);check("Unchecking last role removes only profile rules",p.Services.Count==0);
        p=Policy("ad","file","dfsn");AgentServiceProfiles.Sync(p,observed);check("Shared service has one row and all owners",p.Services.Count(s=>s.Name=="LanmanServer")==1&&p.Services.Single(s=>s.Name=="LanmanServer").Profiles!.Length==3);
        p.ServiceRoles=["file"];AgentServiceProfiles.Sync(p,observed);check("Shared service survives removal of other roles",Has(p,"LanmanServer")&&!Has(p,"NTDS")&&!Has(p,"Dfs"));
        p=Policy("dns");p.Services=[new("DNS",true,"Stopped",Severity.Warning,9),new("BusinessService")];AgentServiceProfiles.Sync(p,observed);p.ServiceRoles=[];AgentServiceProfiles.Sync(p,observed);check("Manual rules preserve expectation severity delay after role off",p.Services.Count==2&&p.Services.Single(s=>s.Name=="DNS") is {Expected:"Stopped",DelayMinutes:9,Profiles:null});
        p=Policy("dns");AgentServiceProfiles.Sync(p,observed);p.Services=[p.Services.Single() with{Enabled=false,Profiles=null}];AgentServiceProfiles.Sync(p,observed);check("Manual exclusion prevents profile recreation",p.Services.Count==1&&!p.Services[0].Enabled);
        p=Policy("ad");p.StandardServices=true;AgentServiceProfiles.Sync(p,observed);p.ServiceRoles=[];AgentServiceProfiles.Sync(p,observed);check("Role off does not re-add AD via baseline",!AgentServiceProfiles.Effective(p,observed).Any(s=>s.Name=="NTDS")&&Has(p,"RpcSs"));
        var legacy=new AgentPolicy();AgentServiceProfiles.Sync(legacy,observed);legacy.RoleProfilesConfigured=true;legacy.ServiceRoles=[];AgentServiceProfiles.Sync(legacy,observed);check("Newly tracked legacy auto AD transitions to explicit roles",!Has(legacy,"NTDS")&&Has(legacy,"RpcSs"));
        legacy=new(){Services=[new("NTDS")]};legacy.RoleProfilesConfigured=true;AgentServiceProfiles.Sync(legacy,observed);check("Old unlabelled rules remain intact",Has(legacy,"NTDS")&&legacy.Services.Single(s=>s.Name=="NTDS").Profiles==null);
        AgentService[] sql=[new("MSSQLSERVER","SQL","Stopped","Automatic"),new("MSSQL$ERP","SQL ERP","Running","Automatic"),new("MSSQL$OLD","Old","Stopped","Disabled"),new("SQLSERVERAGENT","Agent","Stopped","Manual"),new("SQLAgent$ERP","Agent ERP","Stopped","Automatic"),new("SQLBrowser","Browser","Stopped","Manual"),new("SQLAgent$OLD","Old agent","Stopped","Automatic")];
        p=Policy("sql");AgentServiceProfiles.Sync(p,sql);check("SQL detects all active engine instances with exact names",Has(p,"MSSQLSERVER")&&Has(p,"MSSQL$ERP")&&!Has(p,"MSSQL$OLD")&&!Has(p,"SQLAgent$OLD"));
        check("SQL optional Agent and Browser require automatic or running",Has(p,"SQLAgent$ERP")&&!Has(p,"SQLSERVERAGENT")&&!Has(p,"SQLBrowser"));
        q=Policy("sql");AgentServiceProfiles.Sync(q,[new("MSSQL$SQLEXPRESS","SQL Express","Running","Automatic")]);check("SQL Express without Agent has no missing-role warning",AgentServiceProfiles.Findings(q).Length==0&&!Has(q,"SQLSERVERAGENT"));
        q=Policy("sql");AgentServiceProfiles.Sync(q,[]);check("Selected SQL role without engine explains missing discovery",AgentServiceProfiles.Findings(q).Single().Key=="service-role:sql");
        p=Policy("1c");AgentService[] onec=[new("1C:Enterprise 8.3 Server Agent (x86-64)","1C Agent","Running","Automatic"),new("1cv83_ERP","1C ERP","Stopped","Automatic"),new("CustomErpSvc","Агент сервера 1С:Предприятия 8.3","Running","Automatic"),new("1cv83_OLD","Old","Stopped","Disabled"),new("OtherSvc","Not 1c","Running","Automatic")];AgentServiceProfiles.Sync(p,onec);
        check("1C discovers named versions and localized custom service",p.Services.Count==3&&Has(p,"CustomErpSvc")&&!Has(p,"1cv83_OLD"));
        check("1C does not invent SQL or process names as services",!Has(p,"MSSQLSERVER")&&!Has(p,"rphost")&&AgentServiceProfiles.Findings(p,onec).Length==0);
        p.Services.RemoveAll(s=>s.Name!="CustomErpSvc");p.Services=[p.Services[0] with{Profiles=null}];check("Manual localized 1C retains correct discovery",AgentServiceProfiles.Findings(p,onec).Length==0);
        foreach(var tuple in new[]{("dns","DNS"),("dhcp","DHCPServer"),("file","LanmanServer"),("dfsn","Dfs"),("dfsr","DFSR"),("iis","W3SVC"),("hyperv","vmms"),("rds","TermService"),("ca","CertSvc")}){q=Policy(tuple.Item1);AgentServiceProfiles.Sync(q,[]);check("Role main service: "+tuple.Item1,Has(q,tuple.Item2));}
        q=Policy("file");AgentServiceProfiles.Sync(q,[new("SrmSvc","FSRM","Stopped","Automatic"),new("VSS","VSS","Stopped","Manual"),new("Spooler","Print","Stopped","Disabled")]);check("File profile includes FSRM without VSS or print false alarms",Has(q,"SrmSvc")&&!Has(q,"VSS")&&!Has(q,"Spooler")&&!Has(q,"DFSR"));
        q=Policy("rds");AgentServiceProfiles.Sync(q,[new("SessionEnv","RDS","Stopped","Manual"),new("UmRdpService","RDP","Stopped","Manual")]);check("RDS ignores stopped trigger/manual auxiliary services",q.Services.Count==1);
        q=Policy("sql");AgentServiceProfiles.Sync(q,Enumerable.Range(1,100).Select(i=>new AgentService("MSSQL$I"+i,"SQL","Running","Automatic")));check("Large role inventory is bounded and explains omitted services",q.Services.Count==64&&q.ServiceProfileOverflow==36&&AgentServiceProfiles.Findings(q).Any(f=>f.Key=="service-role:limit"));AgentWire.Validate(q);
        var clone=JsonSerializer.Deserialize<AgentPolicy>(JsonSerializer.Serialize(q,AgentWire.Json),AgentWire.Json)!;check("Profile ownership survives JSON persistence",clone.ServiceRoles.SequenceEqual(q.ServiceRoles)&&clone.Services[0].Profiles!.SequenceEqual(q.Services[0].Profiles!));
        clone.ServiceRoles=[];AgentServiceProfiles.Sync(clone,[]);check("Persisted profile can be removed after restart",clone.Services.Count==0&&clone.ServiceProfileOverflow==0);
        p=Policy("ad","file");AgentServiceProfiles.Sync(p,observed);AgentServiceProfiles.Delete(p,"LanmanServer");AgentServiceProfiles.Sync(p,observed);
        check("Deleted shared-role service disappears from both stored and visible rules",!Has(p,"LanmanServer")&&!AgentServiceProfiles.Effective(p,observed).Any(s=>s.Name=="LanmanServer"));
        check("Repeated heartbeat cannot restore explicit deletion",!AgentServiceProfiles.Sync(p,observed)&&!Has(p,"LanmanServer"));
        var deleted=JsonSerializer.Deserialize<AgentPolicy>(JsonSerializer.Serialize(p,AgentWire.Json),AgentWire.Json)!;AgentServiceProfiles.Sync(deleted,observed);check("Service deletion survives policy serialization",!Has(deleted,"LanmanServer")&&deleted.ExcludedServices.Contains("LanmanServer"));
        AgentServiceProfiles.Restore(deleted,"LANMANSERVER");AgentServiceProfiles.Sync(deleted,observed);check("Restore matches names case insensitively and profile returns service",Has(deleted,"LanmanServer")&&deleted.ExcludedServices.Length==0);
        p=new();AgentServiceProfiles.Sync(p,[]);AgentServiceProfiles.Delete(p,"EventLog");AgentServiceProfiles.Sync(p,[]);check("Deleted baseline service is not reintroduced by effective defaults",!AgentServiceProfiles.Effective(p,[]).Any(s=>s.Name=="EventLog"));
        p=Policy();p.Services=[new("Custom",true,"Stopped",Severity.Warning,7)];AgentServiceProfiles.Delete(p,"Custom");check("Deleting manual service removes its row",p.Services.Count==0);AgentServiceProfiles.Delete(p,"CUSTOM");check("Repeated deletion does not duplicate exclusion",p.ExcludedServices.Length==1);
        AgentServiceProfiles.Restore(p,"Custom");p.Services.Add(new("Custom"));AgentServiceProfiles.Sync(p,[]);check("Manual re-add clears deletion and can monitor again",Has(p,"Custom")&&p.ExcludedServices.Length==0);
        p=Policy("sql");AgentServiceProfiles.Sync(p,[new("MSSQL$ERP","SQL","Running","Automatic")]);AgentServiceProfiles.Delete(p,"MSSQL$ERP");AgentServiceProfiles.Sync(p,[]);check("Explicit SQL deletion does not become missing-role warning",!AgentServiceProfiles.Findings(p).Any(f=>f.Key=="service-role:sql"));
        p=Policy();p.Services=Enumerable.Range(0,64).Select(i=>new AgentServiceRule("Manual"+i)).ToList();AgentServiceProfiles.Delete(p,"Manual0");p.Services.Add(new("Replacement"));AgentWire.Validate(p);check("Deleted rules do not consume monitored-rule limit",p.Services.Count==64);
        foreach(var excluded in new[]{new[]{"DNS","dns"},new[]{""},null!,Enumerable.Range(0,257).Select(i=>"Service"+i).ToArray()}){bool rejected=false;try{AgentWire.Validate(new AgentPolicy{ExcludedServices=excluded});}catch(ArgumentException){rejected=true;}check("Invalid deleted-service names rejected",rejected);}
        foreach(var invalid in new[]{new AgentPolicy{ServiceRoles=["bad"],RoleProfilesConfigured=true},new AgentPolicy{ServiceRoles=["ad","ad"],RoleProfilesConfigured=true},new AgentPolicy{ServiceRoles=null!},new AgentPolicy{Services=[new("DNS",Profiles:["bad"])]}}){bool rejected=false;try{AgentWire.Validate(invalid);}catch(ArgumentException){rejected=true;}check("Invalid role metadata rejected",rejected);}
    }
}
