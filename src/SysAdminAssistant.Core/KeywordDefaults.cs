namespace SysAdminAssistant.Core;

public static class KeywordDefaults
{
    public static KeywordProfile For(string provider)
    {
        var p = new KeywordProfile { Provider = provider };
        p.Critical += ";errors;failures;unreachable;unable to;out of space;not enough space;access denied;connection refused;permission denied;corrupt*;не удалось;недостаточно места;отказано в доступе;поврежден*";
        p.Warning += ";warnings;skipped;retrying;low disk space;completed with warnings;пропущен*;повторная попытка;мало места";
        p.Info += ";informational;succeeded;finished successfully;completed successfully;успешно выполнен*";
        p.SuccessResults += ";succeeded;completed successfully;finished successfully;успешно выполнено";
        p.WarningResults += ";completed with warnings;completed with warning;completed with errors;partial success;завершено с предупреждениями";
        p.FailedResults += ";failure;aborted;cancelled;canceled;interrupted;stopped with errors;неудачно;прервано";
        p.Ignore += ";skipped: 0;skipped tasks: 0;skipped files: 0;errors: 0;warnings: 0;failures: 0;failed jobs: 0;failed objects: 0;failed machines: 0;warning tasks: 0;error count: 0;warning count: 0;ошибки: 0;сбоев: 0";
        switch (provider)
        {
            case "Veeam": p.Critical += ";repository unavailable;restore point is corrupted"; p.Warning += ";bottleneck;backup window exceeded"; break;
            case "SQLBackupAndFTP": p.Critical += ";backup failed;upload failed;database backup failed"; p.Warning += ";upload skipped;backup completed with warnings"; break;
            case "Active Backup for Business": p.Critical += ";backup task failed;failed to back up;не выполнена"; p.Warning += ";partially completed;device disconnected;выполнена частично"; p.Info += ";выполнена"; p.SuccessResults += ";выполнена"; p.WarningResults += ";выполнена частично"; p.FailedResults += ";не выполнена"; break;
            case "DSM": p.Critical += ";storage pool crashed;volume crashed;drive failure"; p.Warning += ";storage pool degraded;drive reconnection"; break;
            case "QNAP": p.Critical += ";raid group degraded;volume error;disk failure"; p.Warning += ";smart warning;temperature warning"; break;
            case "QNAP HBS": p.Critical += ";sync job failed;backup job failed;failed to synchronize"; p.Warning += ";files skipped;conflict files"; break;
        }
        return p;
    }
    public static string? Validate(params string[] lists)
    {
        foreach (var list in lists)
        {
            var terms = ReportAnalyzer.Terms(list).ToArray();
            if (terms.Length > 250) return "В одном списке допускается до 250 слов или фраз";
            if (terms.Any(t => t.Length > 200 || t == "*" || t[..Math.Max(0, t.Length - 1)].Contains('*')))
                return "Фраза: до 200 символов. Звёздочка разрешена только в конце непустого слова";
        }
        return null;
    }
}
