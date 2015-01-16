using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Api.Extensions;
using NzbDrone.Api.Mapping;
using NzbDrone.Api.Validation;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.SignalR;


namespace NzbDrone.Api.Commands
{
    public class CommandModule : NzbDroneRestModuleWithSignalR<CommandResource, CommandModel>, IHandle<CommandUpdatedEvent>
    {
        private readonly ICommandService _commandService;
        private readonly IServiceFactory _serviceFactory;

        public CommandModule(ICommandService commandService,
                             IBroadcastSignalRMessage signalRBroadcaster,
                             IServiceFactory serviceFactory)
            : base(signalRBroadcaster)
        {
            _commandService = commandService;
            _serviceFactory = serviceFactory;

            GetResourceById = GetCommand;
            CreateResource = StartCommand;
            GetResourceAll = GetAllCommands;

            PostValidator.RuleFor(c => c.Name).NotBlank();
        }

        private CommandResource GetCommand(int id)
        {
            return _commandService.Get(id).InjectTo<CommandResource>();
        }

        private int StartCommand(CommandResource commandResource)
        {
            var commandType = 
              _serviceFactory.GetImplementations(typeof(Command))
                        .Single(c => c.Name.Replace("Command", "")
                        .Equals(commandResource.Name, StringComparison.InvariantCultureIgnoreCase));

            dynamic command = Request.Body.FromJson(commandType);
            command.Trigger = CommandTrigger.Manual;

            var trackedCommand = (CommandModel)_commandService.PublishCommand(command);
            return trackedCommand.Id;
        }

        private List<CommandResource> GetAllCommands()
        {
            return ToListResource(_commandService.GetStarted());
        }

        public void Handle(CommandUpdatedEvent message)
        {
            if (message.Command.Body.SendUpdatesToClient)
            {
                BroadcastResourceChange(ModelAction.Updated, message.Command.Id);
            }
        }
    }
}