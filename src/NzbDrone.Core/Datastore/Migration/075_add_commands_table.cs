using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(75)]
    public class add_commands_table : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("Commands")
                  .WithColumn("CommandName").AsString().NotNullable()
                  .WithColumn("CommandBody").AsString().NotNullable()
                  .WithColumn("Priority").AsInt32().NotNullable()
                  .WithColumn("Status").AsInt32().NotNullable()
                  .WithColumn("Queued").AsDateTime().NotNullable()
                  .WithColumn("Started").AsDateTime().Nullable()
                  .WithColumn("Ended").AsDateTime().Nullable()
                  .WithColumn("Duration").AsTime().Nullable()
                  .WithColumn("Exception").AsString().Nullable()
                  .WithColumn("Manual").AsBoolean();
        }
    }
}
