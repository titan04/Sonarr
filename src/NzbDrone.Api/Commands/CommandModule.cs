using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Api.Extensions;
using NzbDrone.Api.Mapping;
using NzbDrone.Api.Validation;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Commands.Tracking;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.SignalR;


namespace NzbDrone.Api.Commands
{
    public class CommandModule : NzbDroneRestModuleWithSignalR<CommandResource, Command>, IHandle<CommandUpdatedEvent>
    {
        private readonly ICommandService _commandService;
        private readonly IContainer _container;

        public CommandModule(ICommandService commandService,
                             IBroadcastSignalRMessage signalRBroadcaster,
                             IContainer container)
            : base(signalRBroadcaster)
        {
            _commandService = commandService;
            _container = container;

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
              _container.GetImplementations(typeof(Command))
                        .Single(c => c.Name.Replace("Command", "")
                        .Equals(commandResource.Name, StringComparison.InvariantCultureIgnoreCase));

            dynamic command = Request.Body.FromJson(commandType);
            command.Manual = true;

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