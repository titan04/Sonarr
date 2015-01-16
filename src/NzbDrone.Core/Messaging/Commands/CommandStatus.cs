namespace NzbDrone.Core.Messaging.Commands
{
    public enum CommandStatus
    {
        Queued,
        Started,
        Completed,
        Failed,
        Aborted,
        Cancelled
    }
}
