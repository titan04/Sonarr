using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandRepository : IBasicRepository<CommandModel>
    {
        void Clean();
        void OrphanStarted();
        List<CommandModel> FindCommands(string name);
        List<CommandModel> FindQueuedOrStarted(string name);
        CommandModel Next();
        List<CommandModel> Queued();
        List<CommandModel> Started();
    }

    public class CommandRepository : BasicRepository<CommandModel>, ICommandRepository
    {
        public CommandRepository(IDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void Clean()
        {
            Delete(c => c.EndedAt < DateTime.UtcNow.AddDays(-1));
        }

        public void OrphanStarted()
        {
            var aborted = Query.Where(c => c.Status == CommandStatus.Started).ToList();

            foreach (var command in aborted)
            {
                command.Status = CommandStatus.Orphaned;
            }

            UpdateMany(aborted);
        }

        public List<CommandModel> FindCommands(string name)
        {
            return Query.Where(c => c.Name == name).ToList();
        }

        public List<CommandModel> FindQueuedOrStarted(string name)
        {
            return Query.Where(c => c.Name == name)
                        .AndWhere("[Status] IN (0,1)")
                        .ToList();
        }

        public CommandModel Next()
        {
            return Query.Where(c => c.Status == CommandStatus.Queued)
                        .OrderByDescending(c => c.Priority)
                        .OrderBy(c => c.QueuedAt)
                        .FirstOrDefault();
        }

        public List<CommandModel> Queued()
        {
            return Query.Where(c => c.Status == CommandStatus.Queued);
        }

        public List<CommandModel> Started()
        {
            return Query.Where(c => c.Status == CommandStatus.Started);
        }
    }
}
