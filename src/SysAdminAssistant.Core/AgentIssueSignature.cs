namespace SysAdminAssistant.Core;
public static class AgentIssueSignature
{
    public static string For(AgentFinding f,AgentReport r)
    {
        if(f.Key.StartsWith("update-delivery:"))return r.Linux?.UpdateDelivery?.Signature??f.Key;
        if(f.Key.StartsWith("systemd-job:"))return AgentWire.Hash(f.Key+"|"+f.Title);
        if(f.Key.StartsWith("service:")){var name=f.Key[8..];var unit=r.Linux?.Units.FirstOrDefault(u=>u.Name==name);return AgentWire.Hash(f.Key+"|"+(unit==null?r.Services.FirstOrDefault(s=>s.Name==name)?.Status:unit.Active+"|"+unit.Sub+"|"+unit.Result+"|"+unit.ExitStatus));}
        if(f.Key=="collector")return AgentWire.Hash(f.Key+"|"+string.Join("|",r.Errors.Order(StringComparer.Ordinal)));
        if(f.Key.StartsWith("journal-reader:"))return AgentWire.Hash(f.Key+"|"+string.Join("|",r.EventBatch?.Health.Select(h=>h.Channel+":"+h.State).Order(StringComparer.Ordinal).ToArray()??[]));
        // Numeric samples and timestamps must not create a new signature every poll.
        return AgentWire.Hash(f.Key);
    }
}
