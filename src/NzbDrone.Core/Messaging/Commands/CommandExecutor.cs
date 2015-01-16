using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;

namespace NzbDrone.Core.Messaging.Commands
{
    public class CommandExecutor : IHandle<CommandQueuedEvent>,
                                   IHandle<ApplicationStartedEvent>,
                                   IHandle<ApplicationShutdownRequested>
    {
        private readonly Logger _logger;
        private readonly IServiceFactory _serviceFactory;
        private readonly ICommandService _commandService;
        private readonly IEventAggregator _eventAggregator;
        private readonly TaskFactory _taskFactory;

        private static CancellationTokenSource _cancellationTokenSource;

        public CommandExecutor(IServiceFactory serviceFactory,
                               ICommandService commandService,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            var scheduler = new LimitedConcurrencyLevelTaskScheduler(3);

            _logger = logger;
            _serviceFactory = serviceFactory;
            _commandService = commandService;
            _eventAggregator = eventAggregator;
            _taskFactory = new TaskFactory(scheduler);
        }

        private void ExecuteCommands()
        {
            var command = _commandService.Pop();

            while (command != null)
            {
                try
                {
                    ExecuteCommand<Command>(command);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error occurred while executing task " + command.Name, ex);
                }

                command = _commandService.Pop();
            }
        }

        private void ExecuteCommand<TCommand>(CommandModel command) where TCommand : Command
        {
            var handlerContract = typeof(IExecute<>).MakeGenericType(command.Body.GetType());
            var handler = (IExecute<TCommand>)_serviceFactory.Build(handlerContract);

            _logger.Trace("{0} -> {1}", command.GetType().Name, handler.GetType().Name);

            try
            {
                BroadcastCommandUpdate(command);

                if (!MappedDiagnosticsContext.Contains("CommandId") && command.Body.SendUpdatesToClient)
                {
                    MappedDiagnosticsContext.Set("CommandId", command.Id.ToString());
                }

                handler.Execute((TCommand)command.Body);
                _commandService.Completed(command);
            }
            catch (Exception e)
            {
                _commandService.Failed(command, e);
                throw;
            }
            finally
            {
                BroadcastCommandUpdate(command);
                _eventAggregator.PublishEvent(new CommandExecutedEvent(command));

                if (MappedDiagnosticsContext.Get("CommandId") == command.Id.ToString())
                {
                    MappedDiagnosticsContext.Remove("CommandId");
                }
            }

            _logger.Trace("{0} <- {1} [{2}]", command.GetType().Name, handler.GetType().Name, command.Duration.ToString());
        }
        
        private void BroadcastCommandUpdate(CommandModel command)
        {
            if (command.Body.SendUpdatesToClient)
            {
                _eventAggregator.PublishEvent(new CommandUpdatedEvent(command));
            }
        }

        //TODO: right now this will start one thread then another if another job is queued
        //TODO: start multiple threads when jobs are queued up to our maximum
        public void Handle(CommandQueuedEvent message)
        {
            if (Enum.IsDefined(typeof(TaskCreationOptions), (TaskCreationOptions)0x10))
            {
                _taskFactory.StartNew(ExecuteCommands, TaskCreationOptions.PreferFairness | (TaskCreationOptions)0x10)
                            .LogExceptions();
            }
            else
            {
                _taskFactory.StartNew(ExecuteCommands, TaskCreationOptions.PreferFairness)
                            .LogExceptions();
            }
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _logger.Info("Shutting down task execution");
            _cancellationTokenSource.Cancel(true);
        }
    }
}
