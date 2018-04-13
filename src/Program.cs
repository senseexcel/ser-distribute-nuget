namespace Ser.Distribute
{
    #region Usings
    using Microsoft.Extensions.PlatformAbstractions;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using NLog.Config;
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    #endregion

    public class Program
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        public static void Main(string[] args)
        {
            try
            {
                SetLoggerSettings("App.config");
                logger.Info("SerDistribute running.");

                if (args == null || args.Length == 0)
                {
                    logger.Warn("No Parameter - Path to Results");
                }

                var resultFolder = Path.GetFullPath(args[0]);
                if (!Directory.Exists(resultFolder))
                    throw new Exception($"The result folder {resultFolder} not found.");
                else
                    logger.Info($"The result folder is \"{resultFolder}\".");

                var distribute = new Distribute();
                distribute.Run(resultFolder);

                logger.Info("Finish");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The application could not be run.");
                Environment.ExitCode = 1;
            }
        }

        private static void SetLoggerSettings(string configName)
        {
            var path = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, configName);
            if (!File.Exists(path))
            {
                var root = new FileInfo(path).Directory?.Parent?.Parent?.Parent;
                var files = root.GetFiles("App.config", SearchOption.AllDirectories).ToList();
                path = files.FirstOrDefault()?.FullName;
            }

            logger.Factory.Configuration = new XmlLoggingConfiguration(path, false);
        }
    }
}
