namespace SysAdminAssistant.Core;
public static class ServerUpgradeVersions {
 public static void Validate(Version installed,Version incoming,Version package){
  if(package.Major!=0||package.Minor<6||incoming!=package)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Версия файлов сервера не соответствует установщику. Используйте ServerSetup целиком из одной сборки."));
  if(installed.Major!=0||installed.Minor<6)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Обновление поддерживается с версии 0.6 и новее при сохранённом формате установки."));
  if(incoming<installed)throw new InvalidOperationException(global::SysAdminAssistant.Core.ProductText.Get("Понижение версии запрещено. Установлена ")+installed+global::SysAdminAssistant.Core.ProductText.Get(", в пакете ")+incoming+".");
 }
}
