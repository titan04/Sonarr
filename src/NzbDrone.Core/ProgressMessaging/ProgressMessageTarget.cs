using System;
using NLog.Config;
using NLog;
using NLog.Targets;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Commands.Tracking;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ProgressMessaging
{
    public class ProgressMessageTarget : Target, IHandle<ApplicationStartedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly ICommandService _commandService;
        private static LoggingRule _rule;

        public ProgressMessageTarget(IEventAggregator eventAggregator, ICommandService commandService)
        {
            _eventAggregator = eventAggregator;
            _commandService = commandService;
        }

        protected override void Write(LogEventInfo logEvent)
        {
            var command = GetCurrentCommand();

            if (IsClientMessage(logEvent, command))
            {
                _commandService.SetMessage(command, logEvent.FormattedMessage);
                _eventAggregator.PublishEvent(new CommandUpdatedEvent(command));
            }
        }

        private CommandModel GetCurrentCommand()
        {
            var commandId = MappedDiagnosticsContext.Get("CommandId");

            if (String.IsNullOrWhiteSpace(commandId))
            {
                return null;
            }

            return _commandService.Get(Convert.ToInt32(commandId));
        }

        private bool IsClientMessage(LogEventInfo logEvent, CommandModel command)
        {
            if (command == null || !command.Body.SendUpdatesToClient)
            {
                return false;
            }

            return logEvent.Properties.ContainsKey("Status");
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _rule = new LoggingRule("*", LogLevel.Trace, this);

            LogManager.Configuration.AddTarget("ProgressMessagingLogger", this);
            LogManager.Configuration.LoggingRules.Add(_rule);
            LogManager.ReconfigExistingLoggers();
        }
    }
}
