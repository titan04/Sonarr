namespace NzbDrone.Core.Messaging.Commands
{
    public enum CommandTrigger
    {
        Automated = 0, //TODO: Automated sucks, need a better name when its triggered automatically, but not scheduled
        Manual = 1,
        Scheduled = 2
    }
}
