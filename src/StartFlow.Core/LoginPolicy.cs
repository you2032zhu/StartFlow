namespace StartFlow.Core;

public static class LoginPolicy
{
    // AuthenticationId is stable across app restarts, unlock and sleep; new interactive logon gets a new ID.
    public static bool Handle(AppSettings settings, string session, bool loginEntry)
    {
        if (!loginEntry || settings.LastHandledSession == session) return false;
        settings.LastHandledSession = session;
        if (settings.SkipArmedSession is { } armed && armed != session)
        { settings.SkipArmedSession = null; return false; }
        return true;
    }
    public static void ArmSkip(AppSettings settings, string session) => settings.SkipArmedSession = session;
}
