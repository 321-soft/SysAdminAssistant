using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;

namespace SysAdminAssistant.Server;

public sealed class WorkspaceStore(string dataDirectory)
{
    private readonly object gate=new();
    private string PathFor(UserView user)=>Path.Combine(dataDirectory,"workspaces",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.Id)))+".json");
    public static bool PageAllowed(UserView user,int page)=>page is >=0 and <=12&&(page==12?user.Administrator:AccessRules.Level(user,AccessRules.PageModule(page))>=1);
    public static bool TileAllowed(UserView user,WorkspaceTileInfo tile)=>PageAllowed(user,tile.Page)&&(tile.ExtraModule==null||AccessRules.Level(user,tile.ExtraModule)>=1);
    private static WorkspacePage[] Read(string path)=>File.Exists(path)?JsonSerializer.Deserialize<WorkspacePage[]>(File.ReadAllText(path))??[]:[];
    public WorkspacePage[] Load(UserView actor)
    {
        lock(gate)return Read(PathFor(actor)).Where(p=>PageAllowed(actor,p.Page)).Select(p=>p with {
            Tiles=p.Tiles?.Where(t=>WorkspaceLayout.Find(t.Id) is {} info&&TileAllowed(actor,info)).ToArray()
        }).ToArray();
    }
    public WorkspacePage Save(UserView actor,WorkspacePage page)
    {
        if(!PageAllowed(actor,page.Page))throw new UnauthorizedAccessException("Нет доступа к этому рабочему столу.");
        if(page.Tiles is {} tiles){
            if(tiles.Length>24||tiles.Any(t=>t==null||string.IsNullOrEmpty(t.Id)||WorkspaceLayout.Find(t.Id)==null||!WorkspaceLayout.Fits(t.Id,t.Width,t.Height))||
               tiles.Select(t=>t.Id).Distinct(StringComparer.Ordinal).Count()!=tiles.Length)throw new ArgumentException("Недопустимый набор плиток.");
            if(tiles.Any(t=>!TileAllowed(actor,WorkspaceLayout.Find(t.Id)!)))throw new UnauthorizedAccessException("Нет доступа к одной из плиток.");
        }
        lock(gate){
            var path=PathFor(actor);
            var saved=Read(path).Where(p=>p.Page!=page.Page).Append(page).OrderBy(p=>p.Page).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,JsonSerializer.Serialize(saved));File.Move(temp,path,true);}
            finally{File.Delete(temp);}
            return page;
        }
    }
}
