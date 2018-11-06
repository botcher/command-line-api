﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace System.CommandLine.Suggest
{
    public class SuggestionDispatcher
    {
        private readonly ISuggestionRegistration _suggestionRegistration;
        private readonly ISuggestionStore _suggestionStore;
        private readonly Parser _parser;

        public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(5000);

        public SuggestionDispatcher(ISuggestionRegistration suggestionRegistration, ISuggestionStore suggestionStore = null)
        {
            _suggestionRegistration = suggestionRegistration ?? throw new ArgumentNullException(nameof(suggestionRegistration));
            _suggestionStore = suggestionStore ?? new SuggestionStore();

            _parser = new CommandLineBuilder()
                      .UseHelp()
                      .UseExceptionHandler()
                      .UseDebugDirective()
                      .UseParseDirective()
                      .UseParseErrorReporting()

                      .AddCommand("list",
                                  "Lists apps registered for suggestions",
                                  cmd => cmd
                                         .AddOption("--detailed", "", args => args.ParseArgumentsAs<bool>())
                                         .OnExecute<IConsole, bool>((c, detailed) =>
                                                                        c.Out.WriteLine(List(_suggestionRegistration, detailed))),
                                  argument => argument.None())
                      .AddCommand("get",
                                  "Gets suggestions",
                                  cmd => cmd
                                         .AddOption(new[] { "-e", "--executable" },
                                                    "The executable to ask for argument resolution",
                                                    argument => argument
                                                                .LegalFilePathsOnly()
                                                                .ParseArgumentsAs<string>())
                                         .AddOption(new[] { "-p", "--position" }, "the current character position on the command line",
                                                    position => position.ParseArgumentsAs<string>())
                                         .OnExecute<ParseResult, IConsole>(Get))

                      .AddCommand("register",
                                  "Registers an app for suggestions",
                                  cmd =>
                                  {
                                      cmd.AddOption("--command-path", "The path to the command for which to register suggestions",
                                                    a => a.ParseArgumentsAs<string>())
                                         .AddOption("--suggestion-command", "The command to invoke to retrieve suggestions",
                                                    a => a.ParseArgumentsAs<string>())
                                         .OnExecute<string, string, IConsole>(Register);
                                  })

                      .AddVersionOption()
                      .Build();
        }

        public Task<int> InvokeAsync(string[] args, IConsole console = null) =>
            _parser.InvokeAsync(args, console);

        private void Register(
            string commandPath,
            string suggestionCommand,
            IConsole console)
        {
            var existingRegistration = _suggestionRegistration.FindRegistration(new FileInfo(commandPath));

            if (existingRegistration == null)
            {
                _suggestionRegistration.AddSuggestionRegistration(
                    new RegistrationPair(commandPath, suggestionCommand));

                console.Out.WriteLine($"Registered {commandPath} --> {suggestionCommand}");
            }
            else
            {
                console.Out.WriteLine($"Registered {commandPath} --> {suggestionCommand}");
            }
        }

        private void Get(ParseResult parseResult, IConsole console)
        {
            var commandPath = parseResult.ValueForOption<FileInfo>("-e");

            var suggestionRegistration =
                _suggestionRegistration.FindRegistration(commandPath);

            if (suggestionRegistration == null)
            {
                // Can't find a completion exe to call
#if DEBUG
                Program.LogDebug($"Couldn't find registration for parse result: {parseResult}");
#endif
                return;
            }

            var targetExePath = suggestionRegistration.CommandPath;

            string targetArgs = FormatSuggestionArguments(
                parseResult,
                targetExePath);

            string suggestions = _suggestionStore.GetSuggestions(
                targetExePath,
                targetArgs,
                Timeout);

            console.Out.Write(suggestions);
        }

        private static string List(
            ISuggestionRegistration suggestionProvider,
            bool detailed = false)
        {
            var registrations = suggestionProvider.FindAllRegistrations();

            if (detailed)
            {
                return string.Join(Environment.NewLine,
                                   registrations.Select(r => $"{r.CommandPath} --> {r.SuggestionCommand}"));
            }
            else
            {
                return string.Join(" ", registrations
                                        .Select(suggestionRegistration => suggestionRegistration.CommandPath)
                                        .Select(Path.GetFileNameWithoutExtension));
            }
        }

        private static string FormatSuggestionArguments(
            ParseResult parseResult,
            string targetExeName)
        {
            var outboundArgs = new List<string>
                               {
                                   "[suggest]"
                               };

            var tokens = parseResult.UnparsedTokens;

            var rootCommand = tokens.FirstOrDefault().RemoveExeExtension();

            targetExeName = Path.GetFileName(targetExeName).RemoveExeExtension();

            if (rootCommand == targetExeName)
            {
                tokens = tokens.Skip(1).ToArray();
            }

            outboundArgs.AddRange(tokens);

            return string.Join(' ', outboundArgs);
        }
    }
}