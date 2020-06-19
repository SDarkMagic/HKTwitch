﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using HollowTwitch.Entities;
using HollowTwitch.Entities.Attributes;
using HollowTwitch.Precondition;
using Logger = Modding.Logger;

namespace HollowTwitch
{
    // Scuffed command proccersor thingy, needs a lot of work
    public class CommandProcessor
    {
        private const char Seperator = ' ';

        private readonly Dictionary<Type, IArgumentParser> _parsers;


        public List<Command> Commands { get; }

        public CommandProcessor()
        {
            Commands = new List<Command>();
            _parsers = new Dictionary<Type, IArgumentParser>();
        }

        public void AddTypeParser<T>(T parser, Type t) where T : IArgumentParser
        {
            _parsers.Add(t, parser);
        }

        public void Execute(string command)
        {
            string[] pieces = command.Split(Seperator);

            IOrderedEnumerable<Command> found = Commands
                                                .Where(x => x.Name.Equals(pieces[0], StringComparison.InvariantCultureIgnoreCase))
                                                .OrderByDescending(x => x.Priority);

            foreach (Command c in found)
            {
                // if (!c.Preconditions.All(x => x.Check()))
                //     continue;

                bool allGood = true;

                foreach (PreconditionAttribute p in c.Preconditions)
                {
                    if (p.Check()) continue;

                    allGood = false;

                    if (c.Preconditions.FirstOrDefault() is CooldownAttribute cooldown)
                    {
                        Logger.Log
                        (
                            $"The coodown for command {c.Name} failed. "
                            + $"The cooldown has {cooldown.MaxUses - cooldown.Uses} and will reset in {cooldown.ResetTime - DateTimeOffset.Now}"
                        );
                    }
                }

                if (!allGood)
                    continue;

                IEnumerable<string> args = pieces.Skip(1);

                if (!BuildArguments(args, c, out object[] parsed))
                    continue;

                try
                {
                    Logger.Log($"Built arguments for command {command}.");

                    if (c.MethodInfo.ReturnType == typeof(IEnumerator))
                    {
                        var t = c.MethodInfo.Invoke(c.ClassInstance, parsed) as IEnumerator;
                        GameManager.instance.StartCoroutine(t);
                    }
                    else
                    {
                        c.MethodInfo.Invoke(c.ClassInstance, parsed);
                    }
                }
                catch (Exception e)
                {
                    Logger.Log(e.ToString());
                }
            }
        }

        private bool BuildArguments(IEnumerable<string> args, Command command, out object[] parsed)
        {
            parsed = null;

            // Avoid multiple enumerations when indexing
            string[] enumerated = args.ToArray();
            
            ParameterInfo[] parameters = command.Parameters;

            bool hasRemainder = parameters.Length != 0 && parameters[parameters.Length - 1].GetCustomAttributes(typeof(RemainingTextAttribute), false).Any();
            
            if (enumerated.Length < parameters.Length && !hasRemainder)
                return false;
            
            List<object> built = new List<object>();

            for (int i = 0; i < parameters.Length; i++)
            {
                string toParse = enumerated[i];
                if (i == parameters.Length - 1)
                {
                    if (hasRemainder)
                    {
                        toParse = string.Join(Seperator.ToString(), enumerated.Skip(i).Take(enumerated.Length).ToArray());
                    }
                }
                
                object p = ParseParameter(toParse, parameters[i].ParameterType);

                if (p is null)
                    return false;

                if (parameters[i].GetCustomAttributes(typeof(EnsureParameterAttribute), false).FirstOrDefault() is EnsureParameterAttribute epa)
                    p = epa.Ensure(p);

                built.Add(p);
            }

            parsed = built.ToArray();

            return true;
        }

        private object ParseParameter(string arg, Type type)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(type);

            try
            {
                return converter.ConvertFromString(arg);
            }
            catch
            {
                try
                {
                    return _parsers[type].Parse(arg);
                }
                catch
                {
                    return null;
                }
            }
        }

        public void RegisterCommands<T>()
        {
            MethodInfo[] methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance);

            object instance = Activator.CreateInstance(typeof(T));

            foreach (MethodInfo method in methods)
            {
                HKCommandAttribute attr = method.GetCustomAttributes(typeof(HKCommandAttribute), false).OfType<HKCommandAttribute>().FirstOrDefault();

                if (attr == null)
                    continue;

                Commands.Add(new Command(attr.Name, method, instance));
                Logger.Log($"Added command: {attr.Name}");
            }
        }
    }
}