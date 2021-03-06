﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Mute.Services;
using Newtonsoft.Json;

namespace Mute
{
    class Program
    {
        private readonly CommandService _commands;
        private readonly DiscordSocketClient _client;
        private readonly IServiceProvider _services;

        private readonly Configuration _config;

        #region static main
        static void Main(string[] args) 
        {
            //Sanity check config file exists and early exit
            if (!File.Exists(@"config.json"))
            {
                Console.Write(Directory.GetCurrentDirectory());
                Console.Error.WriteLine("No config file found");
                return;
            }

            //Read config file
            var config = JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(@"config.json"));

            //Run the program
            new Program(config)
                .MainAsync(args)
                .GetAwaiter()
                .GetResult();
        }
        #endregion

        public Program(Configuration config)
        {
            _config = config;

            _commands = new CommandService(new CommandServiceConfig {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async
            });
            _client = new DiscordSocketClient();

            var serviceCollection = new ServiceCollection()
                .AddSingleton(_config)
                .AddSingleton(_commands)
                .AddSingleton(_client)
                .AddSingleton(new DatabaseService(_config.Database))
                .AddSingleton<InteractiveService>()
                .AddSingleton<CatPictureService>()
                .AddSingleton<DogPictureService>()
                .AddSingleton<CryptoCurrencyService>()
                .AddSingleton<IStockService>(new AlphaAdvantageService(config.AlphaAdvantage))
                .AddScoped<Random>();

            _services = serviceCollection.BuildServiceProvider();
        }

        public async Task MainAsync(string[] args)
        {
            await SetupModules();

            // Log the bot in
            await _client.LoginAsync(TokenType.Bot, _config.Auth.Token);
            await _client.StartAsync();

            if (Debugger.IsAttached)
                await _client.SetGameAsync("Debug Mode");

            // Block the program until it is closed.
            Console.WriteLine("Press any key to exit");
            Console.ReadKey(true);

            if (_client.LoginState == LoginState.LoggedIn)
            {
                Console.WriteLine("Exiting");
                await _client.LogoutAsync();
            }
        }

        public async Task SetupModules()
        {
            // Hook the MessageReceived Event into our Command Handler
            _client.MessageReceived += HandleMessage;

            // Discover all of the commands in this assembly and load them.
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly());
            
            // Print loaded modules
            Console.WriteLine($"Loaded Modules ({_commands.Modules.Count()}):");
            foreach (var module in _commands.Modules)
                Console.WriteLine($" - {module.Name}");
        }

        public async Task HandleMessage(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            if (!(messageParam is SocketUserMessage message))
                return;

            // Check if the message starts with the command prefix character
            var prefixPos = 0;
            var hasPrefix = message.HasCharPrefix('!', ref prefixPos);

            // Check if the bot is mentioned in a prefix
            var prefixMentionPos = 0;
            var hasPrefixMention = message.HasMentionPrefix(_client.CurrentUser, ref prefixMentionPos);

            // Check if the bot is mentioned at all
            var mentionsBot = ((IUserMessage)message).MentionedUserIds.Contains(_client.CurrentUser.Id);

            if (hasPrefix || hasPrefixMention)
            {
                //It's a command, process it as such
                await ProcessAsCommand(message, Math.Max(prefixPos, prefixMentionPos));
            }
            else if (mentionsBot)
            {
                //It's not a command, but the bot was mentioned
                Console.WriteLine($"I was mentioned in: '{message.Content}'");
            }
        }

        private async Task ProcessAsCommand(SocketUserMessage message, int offset)
        {
            // Create a Command Context
            var context = new SocketCommandContext(_client, message);

            // When there's a mention the command may or may not include the prefix. Check if it does include it and skip over it if so
            if (context.Message.Content[offset] == '!')
                offset++;

            // Execute the command
            var result = await _commands.ExecuteAsync(context, offset, _services);
            if (!result.IsSuccess)
                await context.Channel.SendMessageAsync(result.ErrorReason);
        }
    }
}