namespace DistributeService
{
    #region Usings
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NLog;
    using NLog.Web;
    #endregion

    /// <summary>
    /// Main entry class
    /// </summary>
    public class Program
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Public Methods
        /// <summary>
        /// Main entry method
        /// </summary>
        /// <param name="args">Arguments</param>
        public static void Main(string[] args)
        {
            try
            {
                //Activate Nlog logger with configuration
                logger = NLogBuilder.ConfigureNLog("App.config").GetCurrentClassLogger();

                //Build config for webserver
                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddEnvironmentVariables()
                    .AddCommandLine(args)
                    .Build();

                //Start the web server
                CreateHostBuilder(args)
                    .UseKestrel()
                    .ConfigureAppConfiguration((builderContext, config) =>
                    {
                        config.AddJsonFile("appsettings.json", optional: false);
                    })
                    .UseConfiguration(config)
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .ConfigureLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                    })
                    .UseNLog()
                    .Build()
                    .Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "License validation service has fatal error.");
            }
            finally
            {
                LogManager.Shutdown();
            }
        }

        /// <summary>
        /// Create a new web host builder
        /// </summary>
        /// <param name="args">Arguments</param>
        /// <returns>WebHostBuilder instance</returns>
        public static IWebHostBuilder CreateHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();
        #endregion
    }
}