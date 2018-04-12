﻿using Discord.Commands;
using HelpfulUtilities.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Discord.Addons.Listeners
{
    public class ListenerService
    {
        public IReadOnlyCollection<ListenerInfo> Listeners => _listeners.ToImmutableList();
        private IList<ListenerInfo> _listeners = new List<ListenerInfo>();

        internal RunMode _runMode = RunMode.Sync;
        internal LogSeverity _logLevel = LogSeverity.Info;
        public event Func<LogMessage, Task> Log;

        internal Func<IUserMessage, ICommandContext> _context;
        internal Func<ICommandContext, IServiceProvider> _services;

        public ListenerService() : this(new ListenerServiceConfig()) { }
        public ListenerService(ListenerServiceConfig config)
        {
            _runMode = config.RunMode;
            _logLevel = config.LogLevel;

            _context = config.ContextFactory;
            _services = config.ServiceProvider == null ? config.ServiceProviderFactory : _ => config.ServiceProvider;

            VerboseAsync($"{nameof(ListenerService)} initialized");
        }

        public IEnumerable<ListenerInfo> AddModules(params Type[] types) => AddModules(types);
        public IEnumerable<ListenerInfo> AddModules(IEnumerable<Type> types) => types.Select(x => AddModule(x)).Flatten();
        public IEnumerable<ListenerInfo> AddModules(Assembly assembly = null)
        {
            assembly = assembly ?? Assembly.GetCallingAssembly();
            VerboseAsync($"Searching for listeners in {assembly.FullName} assembly.");
            return AddModules(assembly.GetTypes().Where(type => type.Extends(typeof(ModuleBase))));
        }

        public IEnumerable<ListenerInfo> AddModule<T>() => AddModule(typeof(T));
        public IEnumerable<ListenerInfo> AddModule(Type type)
        {
            DebugAsync($"Searching for listeners in {type.Name}");
            var list = type.GetMethods().Where(x => x.GetCustomAttribute<ListenerAttribute>() != null)
                .Select(x => new ListenerInfo(this, x));
            DebugAsync($"Found {list.Count()} listeners");
            VerboseAsync($"Preparing to add {list.Count()} listeners");
            _listeners.AddRange(list);
            return list;
        }

        
        public bool RemoveModules(params ListenerInfo[] listeners) => RemoveModules(listeners);
        public bool RemoveModules(IEnumerable<ListenerInfo> listeners)
        {
            VerboseAsync($"Preparing to remove {listeners.Count()} listeners");
            foreach (var listener in listeners)
            {
                if (!RemoveModule(listener))
                    return false;
            }
            DebugAsync($"Removed {listeners.Count()} listeners");
            return true;
        }

        public bool RemoveModule(ListenerInfo listener)
        {
            VerboseAsync($"Preparing to remove {listener.Name}");
            var result = _listeners.Remove(listener);
            if (result)
                DebugAsync($"Removed listener {listener.Name}");
            else
                WarnAsync($"Failed to remove listener {listener.Name}");
            return result;
        }

        public async Task<IEnumerable<IResult>> ExecuteAsync(ICommandContext context, IServiceProvider services)
        {
            await VerboseAsync("Preparing to execute");
            return await Task.WhenAll(_listeners.Select(async x =>
            {
                var result = x.CheckPreconditions(context);
                if (!result.IsSuccess)
                {
                    await DebugAsync($"Preconditions on listener {x.Name} failed");
                    return result;
                }

                result = await x.ExecuteAsync(context, services);
                if (result.IsSuccess)
                    await VerboseAsync($"Executed {x.Name}");
                else
                    await ErrorAsync($"{x.Name} failed to execute: {result.ErrorReason}", Enum.GetName(typeof(CommandError), result.Error));

                return result;
            }));
        }

        public async Task ListenerAsync(IMessage msg)
        {
            if (msg is IUserMessage message)
            {
                await VerboseAsync("Constructing context");
                var context = _context(message);
                await VerboseAsync("Constructing service provider");
                var services = _services(context);

                await DebugAsync("Executing message");
                _ = await ExecuteAsync(context, services);
            }
        }

        private async Task LogInternalAsync(LogSeverity severity, string message, string source, Exception exception)
        {
            if (severity <= _logLevel)
                await Log(new LogMessage(severity, source, message, exception));
        }

        internal Task CriticalAsync(string message, string source = null, Exception exception = null)
            => LogInternalAsync(LogSeverity.Critical, message, source, exception);

        internal Task ErrorAsync(string message, string source = null, Exception exception = null)
            => LogInternalAsync(LogSeverity.Error, message, source, exception);

        internal Task WarnAsync(string message, string source = null, Exception exception = null)
            => LogInternalAsync(LogSeverity.Warning, message, source, exception);

        internal Task LogAsync(string message, string source = null, Exception exception = null)
            => LogInternalAsync(LogSeverity.Info, message, source, exception);

        internal Task VerboseAsync(string message, string source = null, Exception exception = null)
            => LogInternalAsync(LogSeverity.Verbose, message, source, exception);

        internal Task DebugAsync(string message, string source = null, Exception exception = null)
            => LogInternalAsync(LogSeverity.Debug, message, source, exception);

    }
}
