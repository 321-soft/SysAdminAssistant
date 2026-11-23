using SysAdminAssistant.Core;
namespace SysAdminAssistant.Infrastructure;
public sealed record BackupRunRow(SqlBackupJob Entity,int Slot,BackupLamp Latest,BackupLamp Schedule)
{
    public string Title=>Schedule.Name;public string Host=>Entity.Host;public string BackupSystem=>string.IsNullOrEmpty(Entity.BackupSystem)?"Автоопределение":Entity.BackupSystem;
    public string Status=>Schedule.Status;public LampState State=>Latest.State;public LampState ScheduleState=>Schedule.State;public string Result=>Latest.Status;public DateTime? LastReport=>Latest.LastReport;
    // Current schedule and dated historical result must never share an unqualified success label.
    public string CurrentStatus=>Schedule.State==LampState.Green&&Latest.State!=LampState.Green&&Latest.LastReport!=null&&(Schedule.LastReport==null||Latest.LastReport>=Schedule.LastReport)?Latest.Status:Schedule.Status;
    public LampState DisplayState=>!Entity.Active||Entity.Days==0?Schedule.State
        :Schedule.State==LampState.Red||Latest.State==LampState.Red?LampState.Red
        :Schedule.State==LampState.Amber||Latest.State==LampState.Amber?LampState.Amber
        :Schedule.State==LampState.Gray||Latest.State==LampState.Gray?LampState.Gray:LampState.Green;
    public string LastResultDetails=>Latest.LastReport is DateTime at?"Последний отчёт: "+Display.Stamp(at)+" · "+Latest.Status:"Последний отчёт ещё не получен";
    public string Days=>string.Join(", ",new[]{"Пн","Вт","Ср","Чт","Пт","Сб","Вс"}.Where((_,i)=>(Entity.Days&(1<<i))!=0));
    public string Deadline=>TimeSpan.FromMinutes(Slot).ToString(@"hh\:mm")+(Entity.EarlyMinutes is int early?(early==Entity.GraceMinutes?" ± "+early:" −"+early+" / +"+Entity.GraceMinutes):" + "+Entity.GraceMinutes)+" мин";
}
