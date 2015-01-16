using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Commands.Events;
using NzbDrone.Core.Messaging.Commands.Tracking;
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

    public class CommandService : ICommandService
    {
        private readonly ICommandRepository _repo;
        private readonly IServiceFactory _serviceFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private static readonly object Mutex = new object();

        public CommandService(ICommandRepository repo, IServiceFactory serviceFactory, IEventAggregator eventAggregator, Logger logger)
        {
            _repo = repo;
            _serviceFactory = serviceFactory;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public CommandModel PublishCommand<TCommand>(TCommand command) where TCommand : Command
        {
            var commandModel = PublishCommandInternal(command);

            _eventAggregator.PublishEvent(new CommandQueuedEvent());

            return commandModel;
        }

        public CommandModel PublishCommand(string commandName)
        {
            var command = PublishCommand(commandName, null);

            _eventAggregator.PublishEvent(new CommandQueuedEvent());

            return command;
        }

        private CommandModel PublishCommand(string commandName, DateTime? lastExecutionTime)
        {
            dynamic command = GetCommand(commandName);
            command.LastExecutionTime = lastExecutionTime;

            return PublishCommandInternal(command);
        }

        public void PublishScheduledTasks(List<ScheduledTask> scheduledTasks)
        {
            foreach (var scheduledTask in scheduledTasks)
            {
                PublishCommand(scheduledTask.TypeName, scheduledTask.LastExecution);
            }

            _eventAggregator.PublishEvent(new CommandQueuedEvent());
        }

        public CommandModel Pop()
        {
            lock (Mutex)
            {
                var nextCommand = _repo.Queued().OrderByDescending(c => c.Priority).ThenBy(c => c.Queued).FirstOrDefault();

                if (nextCommand == null)
                {
                    _logger.Trace("No queued commands to execute");
                    return null;
                }

                nextCommand.Started = DateTime.UtcNow;
                _repo.Update(nextCommand);

                return nextCommand;
            }
        }

        public CommandModel Get(int id)
        {
            return _repo.Get(id);
        }

        public List<CommandModel> GetStarted()
        {
            return _repo.Started();
        }

        public void SetMessage(CommandModel command, string message)
        {
            throw new NotImplementedException();
        }

        public void Completed(CommandModel command)
        {
            command.Ended = DateTime.UtcNow;
            command.Status = CommandStatus.Completed;
        }

        public void Failed(CommandModel command, Exception e)
        {
            command.Ended = DateTime.UtcNow;
            command.Status = CommandStatus.Failed;
        }

        private CommandModel PublishCommandInternal<TCommand>(TCommand command) where TCommand : Command
        {
            Ensure.That(command, () => command).IsNotNull();

            _logger.Trace("Publishing {0}", command.GetType().Name);

            var existingCommands = _repo.FindCommands(command.Name).Where(c => c.Status == CommandStatus.Queued ||
                                                                               c.Status == CommandStatus.Started).ToList();

            var existing = existingCommands.SingleOrDefault(c => CommandEqualityComparer.Instance.Equals(c.Body, command));

            if (existing != null)
            {
                _logger.Trace("Command is already in progress: {0}", command.GetType().Name);

                //TODO: Return existing command model
                return existing;
            }

            //TODO: store command trigger type
            var commandModel = new CommandModel
            {
                Name = command.Name,
                Body = command,
                Queued = DateTime.UtcNow,
                //Trigger = command,
                Priority = CommandPriority.Normal,
                Status = CommandStatus.Queued
            };

            _repo.Insert(commandModel);

            return commandModel;
        }

        private dynamic GetCommand(string commandName)
        {
            commandName = commandName.Split('.').Last();

            var commandType = _serviceFactory.GetImplementations(typeof(Command))
                                             .Single(c => c.Name.Replace("Command", "").Equals(commandName, StringComparison.InvariantCultureIgnoreCase));

            return Json.Deserialize("{}", commandType);
        }
    }
}
