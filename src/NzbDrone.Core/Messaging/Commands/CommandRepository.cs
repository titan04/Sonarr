using System.Collections.Generic;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandRepository : IBasicRepository<CommandModel>
    {
        void Clean();
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
            throw new System.NotImplementedException();
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
