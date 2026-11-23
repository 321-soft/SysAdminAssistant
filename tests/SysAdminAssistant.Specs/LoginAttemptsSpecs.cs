using SysAdminAssistant.Server;

internal static class LoginAttemptsSpecs
{
    public static void Run(Action<string,bool> check)
    {
        var time=new DateTime(2026,11,10,12,0,0,DateTimeKind.Utc);
        var attempts=new LoginAttempts();
        check("A peer cannot bypass login throttle by rotating usernames",
            Enumerable.Range(0,30).All(_=>attempts.AllowPeer("noisy",time))&&!attempts.AllowPeer("noisy",time));
        check("Other peers and genuine account budgets remain independent",
            attempts.AllowPeer("office",time)&&Enumerable.Range(0,5).All(_=>attempts.AllowAccount("login","admin",time))
            &&!attempts.AllowAccount("login","admin",time)&&attempts.AllowAccount("vault","admin",time));
        for(var i=0;i<4200;i++)attempts.AllowPeer("fixture"+i,time);
        check("Full peer table admits new peers without resetting account limits",
            attempts.AllowPeer("new office",time)&&!attempts.AllowAccount("login","admin",time));
        check("Peer and account budgets renew at the end of the window",
            attempts.AllowPeer("noisy",time.AddMinutes(1))&&attempts.AllowAccount("login","admin",time.AddMinutes(1)));
    }
}
