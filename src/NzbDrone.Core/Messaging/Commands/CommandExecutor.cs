using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;

namespace NzbDrone.Core.Messaging.Commands
{
    public class CommandExecutor : //IHandle<CommandQueuedEvent>,
                                   IHandle<ApplicationStartedEvent>,
                                   IHandle<ApplicationShutdownRequested>
    {
        private readonly Logger _logger;
        private readonly IServiceFactory _serviceFactory;
        private readonly ICommandService _commandService;
        private readonly IEventAggregator _eventAggregator;
        private readonly TaskFactory _taskFactory;

        private static CancellationTokenSource _cancellationTokenSource;
        private const int THREAD_LIMIT = 1;

        public CommandExecutor(IServiceFactory serviceFactory,
                               ICommandService commandService,
                               IEventAggregator eventAggregator,
                               Logger logger)
        {
            var scheduler = new LimitedConcurrencyLevelTaskScheduler(THREAD_LIMIT);

            _logger = logger;
            _serviceFactory = serviceFactory;
            _commandService = commandService;
            _eventAggregator = eventAggregator;
            _taskFactory = new TaskFactory(scheduler);
        }

        private void ExecuteCommands()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                var command = _commandService.Pop();

                if (command != null)
                {
                    try
                    {
                        ExecuteCommand((dynamic)command.Body, command);
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Error occurred while executing task " + command.Name, ex);
                    }
                }
                
                Thread.Sleep(50);
            }
        }

        private void ExecuteCommand<TCommand>(TCommand command, CommandModel commandModel) where TCommand : Command
        {
            var handlerContract = typeof(IExecute<>).MakeGenericType(command.GetType());
            var handler = (IExecute<TCommand>)_serviceFactory.Build(handlerContract);

            _logger.Trace("{0} -> {1}", command.GetType().Name, handler.GetType().Name);

            try
            {
                BroadcastCommandUpdate(commandModel);

                if (!MappedDiagnosticsContext.Contains("CommandId") && command.SendUpdatesToClient)
                {
                    MappedDiagnosticsContext.Set("CommandId", commandModel.Id.ToString());
                }

                handler.Execute(command);
                _commandService.Completed(commandModel);
            }
            catch (Exception e)
            {
                _commandService.Failed(commandModel, e);
                throw;
            }
            finally
            {
                BroadcastCommandUpdate(commandModel);

                _eventAggregator.PublishEvent(new CommandExecutedEvent(commandModel));

                if (MappedDiagnosticsContext.Get("CommandId") == commandModel.Id.ToString())
                {
                    MappedDiagnosticsContext.Remove("CommandId");
                }
            }

            _logger.Trace("{0} <- {1} [{2}]", command.GetType().Name, handler.GetType().Name, commandModel.Duration.ToString());
        }
        
        private void BroadcastCommandUpdate(CommandModel command)
        {
            if (command.Body.SendUpdatesToClient)
            {
                _eventAggregator.PublishEvent(new CommandUpdatedEvent(command));
            }
        }

        // TODO: We should use async await (once we get 4.5) or normal Task Continuations on Command processing to prevent blocking the TaskScheduler.
        //       For now we use TaskCreationOptions 0x10, which is actually .net 4.5 HideScheduler.
        //       This will detach the scheduler from the thread, causing new Task creating in the command to be executed on the ThreadPool, avoiding a deadlock.
        //       Please note that the issue only shows itself on mono because since Microsoft .net implementation supports Task inlining on WaitAll.
        public void Handle(ApplicationStartedEvent message)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            if (Enum.IsDefined(typeof(TaskCreationOptions), (TaskCreationOptions)0x10))
            {
                for (int i = 0; i < THREAD_LIMIT; i++)
                {
                    _taskFactory.StartNew(ExecuteCommands, TaskCreationOptions.PreferFairness | (TaskCreationOptions)0x10 | TaskCreationOptions.LongRunning)
                            .LogExceptions();
                }
            }
            else
            {
                for (int i = 0; i < THREAD_LIMIT; i++)
                {
                    _taskFactory.StartNew(ExecuteCommands, TaskCreationOptions.PreferFairness | TaskCreationOptions.LongRunning)
                            .LogExceptions();
                }
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _logger.Info("Shutting down task execution");
            _cancellationTokenSource.Cancel(true);
        }
    }
}
