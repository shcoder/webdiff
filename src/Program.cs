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
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using NDesk.Options;
using Newtonsoft.Json;
using OpenQA.Selenium.Remote;
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
                /*.ConfigureHostConfiguration(configHost =>
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
                })*/
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    configLogging
                        .ClearProviders()
                        .AddConsole()
                        .AddInMemory()
                        .SetMinimumLevel(LogLevel.Debug)
                        ;
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseUrls("http://0.0.0.0:55580", "https://0.0.0.0:55443/");
                })
                .UseConsoleLifetime()
                .Build();

            await host.RunAsync();
            return ProgramService.Result;
        }
    }

    internal class ProgramService : IHostedService
    {
        private readonly IServiceProvider provider;
        private readonly ILogger<ProgramService> log;
        public static int Result;
        private CancellationToken cancelationToken;
        private Task task;

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
            this.cancelationToken = cancellationToken;
            task = new Task(() => Result = Main(), cancellationToken, TaskCreationOptions.LongRunning);
            task.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews().AddNewtonsoftJson();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "WebDiffApplication", Version = "v1" });
            });

            //services.AddSingleton<PubSub.Hub>(sp => new PubSub.Hub());
            //services.AddDatabaseDeveloperPageExceptionFilter();

            services
                //.AddLogging()
                .AddScoped<Session>()
                .AddScoped<Processor>()
                //.AddHostedService<ProgramService>()
                ;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (true /*env.IsDevelopment()*/)
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "webdiff v1"));
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            //app.UseMiddleware<RequestLoggingMiddleware>();

            //app.UseHttpsRedirection();
            Console.WriteLine(env.ContentRootPath);
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(
                    Path.Combine(env.ContentRootPath, "wwwroot")),
                //RequestPath = "/StaticFiles"
            });

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }

}