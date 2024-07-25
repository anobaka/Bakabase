namespace Bakabase.Modules.ThirdParty.Abstractions.Logging
{
    public enum ThirdPartyRequestResultType
    {
        Succeed = 1,
        TimedOut = 2,
        Banned = 3,
        Canceled = 4,
        Failed = 1000
    }
}