﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.CommandLineUtils;
using DBMigrator.Model;
using System.Collections.Generic;

namespace DBMigrator.Console
{
    public class Program
    {
        private static ILogger _logger;

        public static void Main(string[] args)
        {
            CommandLineApplication commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: false);
            commandLineApplication.HelpOption("-? | -h | --help");
            CommandArgument names = null;
            var test2 = commandLineApplication.Command("name",
                (target) =>
                names = target.Argument(
                    "fullname",
                    "Enter the full name of the person to be greeted.",
                    multipleValues: true));
            test2.HelpOption("-? | -h | --help");
            var commandArg = commandLineApplication.Argument("command <upgrade|downgrade|validate>", "Command to execute");

            CommandOption versionArg = commandLineApplication.Option(
                "-v |--version <version>",
                "The target version of the migration. If left blank targets latest version",
                CommandOptionType.SingleValue);

            CommandOption serveraddressArg = commandLineApplication.Option(
                "-s |--serveraddress <serveraddress>",
                "The address of the database server instance",
                CommandOptionType.SingleValue);

            CommandOption databasenameArg = commandLineApplication.Option(
                "-d |--databasename <databasename>",
                "The databasename of the database to migrate",
                CommandOptionType.SingleValue);

            CommandOption usernameArg = commandLineApplication.Option(
                "-u |--username <username>",
                "The username of user to auth against the database",
                CommandOptionType.SingleValue);

            CommandOption passwordArg = commandLineApplication.Option(
                "-p |--password <password>",
                "The password for the to auth against the database",
                CommandOptionType.SingleValue);

            CommandOption noPromptArg = commandLineApplication.Option(
                "--noprompt",
                "Runs command without required user interaction",
                CommandOptionType.NoValue);

            IServiceCollection serviceCollection = new ServiceCollection();
            Bootstrapper.ConfigureServices(serviceCollection);

            var test = serviceCollection.BuildServiceProvider();
            var loggerFactory = test.GetRequiredService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<Program>();

            test2.OnExecute(() => {
                return 0;
            });

            commandLineApplication.OnExecute(() =>
            {
                switch (commandArg.Value)
                {
                    case "upgrade":
                        Upgrade(versionArg.Value(), serveraddressArg.Value(), databasenameArg.Value(), usernameArg.Value(), passwordArg.Value(), noPromptArg.HasValue());
                        break;
                    case "downgrade":
                        Rollback(versionArg.Value(), serveraddressArg.Value(), databasenameArg.Value(), usernameArg.Value(), passwordArg.Value());
                        break;
                    case "validate":
                        Validate(serveraddressArg.Value(), databasenameArg.Value(), usernameArg.Value(), passwordArg.Value());
                        break;
                    default:
                        break;
                }
                return 0;
            });
            commandLineApplication.Execute(args);
        }

        private static void Upgrade(string toVersion, string servername, string databasename, string username, string password, bool noPrompt = false)
        {
            var database = new Database(servername, databasename, username, password);
            var dbfolder = new DBFolder();
            _logger.LogDebug($"Reading from {DBFolder.GetExecutingDir().FullName}");
            var validator = new VersionValidator();
            var dbVersions = database.GetDBState();
            validator.ValidateVersions(dbfolder.allVersions, dbVersions);
            var differ = new VersionDiff();

            var diff = differ.Diff(dbfolder.GetVersions(toVersion), dbVersions);

            var diffText = differ.DiffText(diff);
            _logger.LogInformation(diffText);
            if(!noPrompt)
                System.Console.ReadKey();
            var migrator = new Migrator(database, dbfolder);
            migrator.Upgrade(diff);
            if (!noPrompt)
                System.Console.ReadKey();
        }

        private static void Rollback(string toVersion, string servername, string databasename, string username, string password)
        {
            var database1 = new Database(servername, databasename, username, password);
            var dbVersions1 = database1.GetDBState();
            var dbfolder1 = new DBFolder();
            var validator1 = new VersionValidator();
            validator1.ValidateVersions(dbfolder1.allVersions, dbVersions1);
            var differ1 = new VersionDiff();
            var diff1 = differ1.Diff(dbVersions1, dbfolder1.GetVersions(toVersion));
            dbfolder1.AddRollbacks(diff1);
            var diffText1 = differ1.DiffText(diff1);
            _logger.LogInformation(diffText1);
            System.Console.ReadKey();
            var migrator = new Migrator(database1, dbfolder1);
            migrator.Rollback(diff1);
            System.Console.ReadKey();
        }

        private static void Validate(string servername, string databasename, string username, string password)
        {
            var database2 = new Database(servername, databasename, username, password);
            var dbVersions2 = database2.GetDBState();
            var dbfolder2 = new DBFolder();
            var validator2 = new VersionValidator();
            validator2.ValidateVersions(dbfolder2.allVersions, dbVersions2);
            System.Console.ReadKey();
        }
    }
}
