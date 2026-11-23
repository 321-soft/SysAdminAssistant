namespace SysAdminAssistant.Core;
// Each view owns its gate. A parent awaiting a modal dialog cannot block the dialog's actions.
public sealed class AsyncActionGate
{
    private int running;
    public async Task<bool> TryRunAsync(Func<Task> action)
    {
        if(Interlocked.CompareExchange(ref running,1,0)!=0)return false;
        try{await action();return true;}finally{Volatile.Write(ref running,0);}
    }
}
