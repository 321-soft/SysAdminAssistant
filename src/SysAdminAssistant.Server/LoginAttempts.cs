namespace SysAdminAssistant.Server;

internal sealed class LoginAttempts
{
    private readonly object sync=new();
    private readonly Budget<string> peers=new(4096,30);
    private readonly Budget<(string Operation,string Account)> accounts=new(1024,5);
    public bool AllowPeer(string address,DateTime now){lock(sync)return peers.Take(address,now);}
    public bool AllowAccount(string operation,string id,DateTime now){lock(sync)return accounts.Take((operation,id),now);}

    private sealed class Budget<TKey>(int capacity,int limit) where TKey:notnull
    {
        private sealed class Entry(TKey key,DateTime until)
        {
            public TKey Key{get;}=key;
            public DateTime Until{get;}=until;
            public int Count=1;
        }
        private readonly LinkedList<Entry> windows=new();
        private readonly Dictionary<TKey,LinkedListNode<Entry>> entries=new();
        private void Remove(LinkedListNode<Entry> node){entries.Remove(node.Value.Key);windows.Remove(node);}
        public bool Take(TKey key,DateTime now)
        {
            while(windows.First is {} first&&first.Value.Until<=now)Remove(first);
            if(entries.TryGetValue(key,out var current)){
                if(current.Value.Count>=limit)return false;
                current.Value.Count++;return true;
            }
            // A new source can never be rejected just because the bounded table is full.
            if(entries.Count==capacity)Remove(windows.First!);
            entries.Add(key,windows.AddLast(new Entry(key,now.AddMinutes(1))));
            return true;
        }
    }
}
