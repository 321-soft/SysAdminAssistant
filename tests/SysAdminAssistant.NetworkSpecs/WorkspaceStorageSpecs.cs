using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
using SysAdminAssistant.Server;

internal static class WorkspaceStorageSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var folder=Directory.CreateTempSubdirectory("saa-workspace-");
        try{
            var user=new UserView("user-one","first",false,true,1,new(){["overview"]=1,["devices"]=1,["payments"]=1});
            var other=user with{Id="user-two"};
            var store=new WorkspaceStore(folder.FullName);
            var input=new WorkspacePage(0,[new("payments-week",2,4),new("devices-down",4,2)],true,true,true);
            store.Save(user,input);
            var restored=new WorkspaceStore(folder.FullName).Load(user).Single();
            check("Workspace persists order, large sizes, collapsed state and fill modes for its owner",restored.Page==0&&restored.Collapsed&&restored.FillRows&&restored.FillHeight&&restored.Tiles!.SequenceEqual(input.Tiles!));
            check("Workspace data is isolated between users",store.Load(other).Length==0);
            var restricted=user with{Permissions=new(){["overview"]=1,["devices"]=1}};
            check("A revoked module removes its tiles from the response",store.Load(restricted).Single().Tiles!.Select(t=>t.Id).SequenceEqual(new[]{"devices-down"}));
            bool rejected=false;
            try{store.Save(restricted,input);}catch(UnauthorizedAccessException){rejected=true;}
            check("Server rejects a tile from an inaccessible module",rejected);
            store.Save(user,new(1,[],false));
            store.Save(user,new(0,null));
            var pages=store.Load(user);
            check("Reset restores defaults while an intentionally empty page stays empty",pages[0].Tiles==null&&!pages[0].FillRows&&!pages[0].FillHeight&&pages[1].Tiles is {Length:0});
        }finally{folder.Delete(true);}
    }
}
