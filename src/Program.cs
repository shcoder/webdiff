using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NDesk.Options;
using Newtonsoft.Json;
using OpenQA.Selenium.Remote;
using webdiff.driver;
using webdiff.http;
using webdiff.img;
using webdiff.utils;
using Cookie = OpenQA.Selenium.Cookie;

namespace webdiff
{
    internal static class Program
    {
        private async static Task<int> Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                    //configHost.AddJsonFile(_hostsettings, optional: true);
                    //configHost.AddEnvironmentVariables(prefix: _prefix);
                })
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.SetBasePath(Directory.GetCurrentDirectory());
                    //configApp.AddJsonFile(_appsettings, optional: true);
                    configApp.AddJsonFile(
                        $"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json",
                        optional: true);
                    //configApp.AddEnvironmentVariables(prefix: _prefix);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services
                        .AddLogging()
                        .AddScoped<Session>()
                        .AddScoped<Processor>()
                        //services.Configure<Application>(hostContext.Configuration.GetSection("application"));
                        .AddHostedService<ProgramService>();
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging
                        .SetMinimumLevel(LogLevel.Information)
                        /*.AddFile(o =>
                        {
                            o.RootPath = AppContext.BaseDirectory;
                            o.Files = new LogFileOptions[]
                            {
                                new LogFileOptions() {Path = "test.log"}
                            };
                        })*/
                        .AddConsole()
                        .AddInMemory()
                        ;
                })
                //.ConfigureWebHostDefaults()
                .UseConsoleLifetime()
                .Build();

            await host.RunAsync();
            return ProgramService.Result;
        }
    }

    internal class ProgramService : IHostedService
    {
        protected readonly IServiceProvider provider;
        private readonly ILogger<ProgramService> log;
        public static int Result;

        public ProgramService(IServiceProvider provider, ILogger<ProgramService> log)
        {
            this.provider = provider;
            this.log = log;
        }

        private int Main()
        {
            using var scope = provider.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<Session>();
            session.LoadConfiguration(Environment.GetCommandLineArgs().Skip(1).ToArray());
            if (session.Error != Error.None)
                return (int) session.Error;

            using var processor = scope.ServiceProvider.GetRequiredService<Processor>();
            if (!processor.Init())
                return (int) session.Error;
            if (!processor.LoadCookies())
                return (int) session.Error;

            try
            {
                processor.RunChecks();
            }
            catch (Exception e)
            {
                log.LogError($"Error: {e.GetType().FullName} - {e.Message}");
                return (int) Error.CestLaVie;
            }

            return 0;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Result = Main();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}