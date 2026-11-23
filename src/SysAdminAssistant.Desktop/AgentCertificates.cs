using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using SysAdminAssistant.Core;
using SysAdminAssistant.Infrastructure;
namespace SysAdminAssistant.Desktop;
public partial class MainWindow
{
    private Task ManageAgentCertificates(Device device,Window owner)
    {
        var form=new FormDialog(owner,"Сертификаты · "+device.Name,740)
            .Section("Сертификат агента").Fact("Продление","Автоматически, без переустановки")
            .Note("После долгого отключения сервер проверяет прежний подтверждённый ключ и выдаёт новый сертификат. Отозванные и неподтверждённые агенты доступа не получают.")
            .Section("Доверие HTTPS-серверу").Fact("Обновление","Агент и автообновлятор получают подписанные настройки доверия")
            .Note("Подтверждения агентов и время последнего отчёта указаны в карточке сертификата.");
        form.ActionAsync("Сертификат и доверие сервера",ShowServerCertificate);
        form.ViewOnly().ShowDialog();return Task.CompletedTask;
    }
}
