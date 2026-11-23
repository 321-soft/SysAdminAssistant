using System.Windows;
using SysAdminAssistant.Core;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private async Task DeleteCatalogEntity(Entity entity,string title)
    {
        if(!Confirm(this,$"Удалить «{title}»?\nОперацию нельзя отменить. Если есть история или связанные записи, сервер запретит удаление и сохранит данные."))return;
        await using var db=factory.Create();db.Remove(entity);await db.SaveChangesAsync();
        await remote.RefreshAsync(lifetime.Token);await ShowPage(page);
    }
}
