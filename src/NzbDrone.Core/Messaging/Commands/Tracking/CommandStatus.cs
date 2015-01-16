namespace NzbDrone.Core.Messaging.Commands.Tracking
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
