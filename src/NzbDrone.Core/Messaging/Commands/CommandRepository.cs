using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandRepository : IBasicRepository<CommandModel>
    {
        void Clean();
        void UpdateAborted();
        List<CommandModel> FindCommands(string name);
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
            Delete(c => c.Ended < DateTime.UtcNow.AddDays(-1));
        }

        public void UpdateAborted()
        {
            var aborted = Query.Where(c => c.Status == CommandStatus.Started).ToList();

            foreach (var command in aborted)
            {
                command.Status = CommandStatus.Aborted;
            }

            UpdateMany(aborted);
        }

        public List<CommandModel> FindCommands(string name)
        {
            return Query.Where(c => c.Name == name).ToList();
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
