using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NDesk.Options;
using OpenQA.Selenium;
using webdiff.utils;

namespace webdiff
{
    public enum Error
    {
        None = 0,
        InvalidArgs = 1,
        CestLaVie = 2,
        Exit = 3
    }

    internal class Session
    {
        private readonly ILogger<Session> log;

        public Session(ILogger<Session> log)
        {
            this.log = log;
            Output = ".";
            Profile = "profile.toml";
            Template = "template.html";
            ShowHelpAndExit = false;
            Error = Error.None;
            Started = DateTime.Now;
        }

        public string Output;
        public string Profile;
        public string Template;
        public bool ShowHelpAndExit;
        public Error Error;
        public Settings Settings;
        public Uri SvcLeft;
        public Uri SvcRight;
        public string Input;
        public DateTime Started;
        public Cookie[] Cookies { get; protected set; }

        public Session LoadConfiguration(string[] args)
        {
            var options = new OptionSet
            {
                {"o|output=", $"Reports output directory\n(default: '{Output}')", v => Output = v},
                {
                    "p|profile=", $"Profile TOML file with current settings\n(default: '{Profile}')",
                    v => Profile = v
                },
                {"t|template=", $"HTML report template file\n(default: '{Template}')", v => Template = v},
                {"h|help", "Show this message", v => ShowHelpAndExit = v != null}
            };

            List<string> free = null;
            try
            {
                free = options.Parse(args);
            }
            catch (OptionException e)
            {
                log.LogError($"Option '{e.OptionName}' value is invalid: {e.Message}");
                Error = Error.InvalidArgs;
                return this;
            }

            if (!PathHelper.FileExistsWithOptionalExt(ref Profile, ".toml"))
            {
                log.LogError($"Profile '{Profile}' not found");
                Error = Error.InvalidArgs;
                return this;
            }

            if (!PathHelper.FileExistsWithOptionalExt(ref Template, ".html"))
            {
                log.LogError($"Template '{Template}' not found");
                Error = Error.InvalidArgs;
                return this;
            }

            try
            {
                Settings = Settings.Read(Profile.RelativeToBaseDirectory());
            }
            catch (Exception e)
            {
                log.LogError($"Failed to read profile '{Profile}': {e.Message}");
                Error = Error.InvalidArgs;
                return this;
            }

            try
            {
                if (free?.Count >= 2)
                {
                    SvcLeft = new Uri(free[0]);
                    SvcRight = new Uri(free[1]);
                }
            }
            catch (Exception e)
            {
                log.LogError(e.Message);
                Error = Error.InvalidArgs;
                return this;
            }

            Input = free?.Skip(2).FirstOrDefault();
            if (Input != null && !File.Exists(Input.RelativeToBaseDirectory()))
            {
                log.LogError("Input file with URLs not found");
                Error = Error.InvalidArgs;
            }

            if (ShowHelpAndExit || Error != Error.None || Settings == null || free == null || free.Count < 2)
            {
                PrintUsageInfo(options);
                Error = Error.Exit;
                return this;
            }

            return this;
        }

        public Cookie[] LoadCookies()
        {
            try
            {
                if (Settings.Driver.Cookies != null)
                    Cookies = CookieParser.Parse(Settings.Driver.Cookies.RelativeToBaseDirectory());
            }
            catch (Exception e)
            {
                log.LogError($"Failed to parse Cookies file: {e.Message}");
                Error = Error.InvalidArgs;
            }

            return Cookies;
        }

        public void LogResult(bool success, string prefix, string text = null)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write(prefix);
                Console.ResetColor();
                Console.WriteLine(text);
            }
        }

        private static void PrintUsageInfo(OptionSet options)
        {
            Console.WriteLine("Usage: webdiff [OPTIONS] URL1 URL2 [FILE]");
            Console.WriteLine("Options:");
            options.WriteOptionDescriptions(Console.Out);
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  webdiff http://prod.example.com http://test.example.com < URLs.txt");
            Console.WriteLine(
                "  webdiff -p profile.toml -t template.html -o data http://prod.example.com http://test.example.com URLs.txt");
            Console.WriteLine();
        }
    }
}