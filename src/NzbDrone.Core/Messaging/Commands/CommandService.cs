using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandService
    {
        CommandModel PublishCommand<TCommand>(TCommand command) where TCommand : Command;
        CommandModel PublishCommand(string commandName);
        void PublishScheduledTasks(List<ScheduledTask> commands);
        CommandModel Pop();
        CommandModel Get(int id);
        List<CommandModel> GetStarted(); 
        void SetMessage(CommandModel command, string message);
        void Completed(CommandModel command);
        void Failed(CommandModel command, Exception e);
    }

    public class CommandService : ICommandService, IHandle<ApplicationStartedEvent>
    {
        private readonly ICommandRepository _repo;
        private readonly IServiceFactory _serviceFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private ICached<string> _messageCache; 

        private static readonly object Mutex = new object();

        public CommandService(ICommandRepository repo, 
                              IServiceFactory serviceFactory,
                              IEventAggregator eventAggregator,
                              ICacheManager cacheManager,
                              Logger logger)
        {
            _repo = repo;
            _serviceFactory = serviceFactory;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _messageCache = cacheManager.GetCache<string>(GetType());
        }

        public CommandModel PublishCommand<TCommand>(TCommand command) where TCommand : Command
        {
            Ensure.That(command, () => command).IsNotNull();

            _logger.Trace("Publishing {0}", command.GetType().Name);

            var existingCommands = _repo.FindCommands(command.Name).Where(c => c.Status == CommandStatus.Queued ||
                                                                               c.Status == CommandStatus.Started).ToList();

            var existing = existingCommands.SingleOrDefault(c => CommandEqualityComparer.Instance.Equals(c.Body, command));

            if (existing != null)
            {
                _logger.Trace("Command is already in progress: {0}", command.GetType().Name);

                return existing;
            }

            var commandModel = new CommandModel
            {
                Name = command.Name,
                Body = command,
                Queued = DateTime.UtcNow,
                Trigger = command.Trigger,
                Priority = CommandPriority.Normal,
                Status = CommandStatus.Queued
            };

            _repo.Insert(commandModel);

            return commandModel;
        }

        public CommandModel PublishCommand(string commandName)
        {
            var command = PublishCommand(commandName, null);

            return command;
        }

        public void PublishScheduledTasks(List<ScheduledTask> scheduledTasks)
        {
            foreach (var scheduledTask in scheduledTasks)
            {
                PublishCommand(scheduledTask.TypeName, scheduledTask.LastExecution, CommandTrigger.Scheduled);
            }
        }

        public CommandModel Pop()
        {
            lock (Mutex)
            {
                var nextCommand = _repo.Queued().OrderByDescending(c => c.Priority).ThenBy(c => c.Queued).FirstOrDefault();

                if (nextCommand == null)
                {
                    return null;
                }

                nextCommand.Started = DateTime.UtcNow;
                nextCommand.Status = CommandStatus.Started;

                _repo.Update(nextCommand);

                return nextCommand;
            }
        }

        public CommandModel Get(int id)
        {
            return FindMessage(_repo.Get(id));
        }

        public List<CommandModel> GetStarted()
        {
            return _repo.Started();
        }

        public void SetMessage(CommandModel command, string message)
        {
            _messageCache.Set(command.Id.ToString(), message);
        }

        public void Completed(CommandModel command)
        {
            command.Ended = DateTime.UtcNow;
            command.Duration = command.Ended.Value.Subtract(command.Started.Value);
            command.Status = CommandStatus.Completed;

            _repo.Update(command);

            _messageCache.Remove(command.Id.ToString());
        }

        public void Failed(CommandModel command, Exception e)
        {
            command.Ended = DateTime.UtcNow;
            command.Duration = command.Ended.Value.Subtract(command.Started.Value);
            command.Status = CommandStatus.Failed;

            _repo.Update(command);

            _messageCache.Remove(command.Id.ToString());
        }

        private dynamic GetCommand(string commandName)
        {
            commandName = commandName.Split('.').Last();

            var commandType = _serviceFactory.GetImplementations(typeof(Command))
                                             .Single(c => c.Name.Equals(commandName, StringComparison.InvariantCultureIgnoreCase));

            return Json.Deserialize("{}", commandType);
        }

        private CommandModel PublishCommand(string commandName, DateTime? lastExecutionTime, CommandTrigger trigger = CommandTrigger.Unspecified)
        {
            dynamic command = GetCommand(commandName);
            command.LastExecutionTime = lastExecutionTime;
            command.Trigger = trigger;

            return PublishCommand(command);
        }

        private CommandModel FindMessage(CommandModel command)
        {
            command.Message = _messageCache.Find(command.Id.ToString());

            return command;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _repo.UpdateAborted();
        }
    }
}
