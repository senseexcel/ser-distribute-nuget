namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Q2g.HelperQlik;
    using System.Threading;
    using Ser.Api;
    #endregion

    public class DistributeManager
    {
        #region Logger
        private readonly static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        public string ErrorMessage { get; private set; }
        #endregion

        private T GetSettings<T>(JToken json, bool typeOnly = false) where T : ISettings, new()
        {
            try
            {
                if (typeOnly)
                {
                    var active = json?.Children()["active"]?.ToList()?.FirstOrDefault()?.ToObject<bool>() ?? null;
                    var jProperty = json as JProperty;
                    switch (jProperty?.Name)
                    {
                        case "mail":
                            return new T() { Type = SettingsType.MAIL, Active = active };
                        case "hub":
                            return new T() { Type = SettingsType.HUB, Active = active };
                        case "file":
                            return new T() { Type = SettingsType.FILE, Active = active };
                    }
                }

                return JsonConvert.DeserializeObject<T>(json.First().ToString());
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                return default;
            }
        }

        public string Run(string resultFolder, DistibuteOptions options)
        {
            try
            {
                logger.Info("Read json result files...");
                var jobResults = new List<JobResult>();
                string[] jsonPaths = Directory.GetFiles(resultFolder, "*.json", SearchOption.TopDirectoryOnly);
                foreach (var jsonPath in jsonPaths)
                {
                    if (!File.Exists(jsonPath))
                    {
                        logger.Error($"The json result path \"{jsonPath}\" not found.");
                        continue;
                    }
                    var json = File.ReadAllText(jsonPath);
                    var result = JsonConvert.DeserializeObject<JobResult>(json);
                    foreach (var report in result.Reports)
                    {
                        foreach (var path in report.Paths)
                        {
                            var data = File.ReadAllBytes(path);
                            report.Data.Add(new ReportData()
                            {
                                Filename = Path.GetFileName(path),
                                DownloadData = data
                            });
                        }
                    }
                    jobResults.Add(result);
                }
                return Run(jobResults, options);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Can´t read job results from path.");
                return null;
            }
        }

        public string Run(List<JobResult> jobResults, DistibuteOptions options)
        {
            var results = new DistributeResults();
            var connectionManager = new ConnectionManager();

            try
            {
                var execute = new ExecuteManager();
                logger.Info("Read job results...");
                foreach (var jobResult in jobResults)
                {
                    //Check Cancel
                    options.CancelToken?.ThrowIfCancellationRequested();

                    if (jobResult.Status != TaskStatusInfo.SUCCESS)
                    {
                        logger.Warn($"The result \"{jobResult.Status }\" of the report is not correct. The report is ignored.");
                        continue;
                    }

                    var mailList = new List<MailSettings>();
                    var uploadTasks = new List<Task<HubResult>>();
                    foreach (var report in jobResult.Reports)
                    {
                        //Check Cancel
                        options.CancelToken?.ThrowIfCancellationRequested();

                        var distribute = report?.Distribute ?? null;
                        var resolver = new CryptoResolver(options.PrivateKeyPath);
                        distribute = resolver.Resolve(distribute);
                        var locations = distribute?.Children().ToList() ?? new List<JToken>();
                        foreach (var location in locations)
                        {
                            //Check Cancel
                            options.CancelToken?.ThrowIfCancellationRequested();

                            var settings = GetSettings<BaseDeliverySettings>(location, true);
                            if (settings.Active ?? true)
                            {
                                switch (settings.Type)
                                {
                                    case SettingsType.FILE:
                                        //Copy reports
                                        logger.Info("Check - Copy Files...");
                                        var fileSettings = GetSettings<FileSettings>(location);
                                        fileSettings.Type = SettingsType.FILE;
                                        var fileConfigs = JsonConvert.DeserializeObject<List<SerConnection>>(JsonConvert.SerializeObject(fileSettings?.Connections ?? new List<SerConnection>()));
                                        var fileConnection = connectionManager.GetConnection(fileConfigs);
                                        if (fileConnection == null)
                                            throw new Exception("Could not create a connection to Qlik. (FILE)");
                                        results.FileResults.AddRange(execute.CopyFile(fileSettings, report, fileConnection));
                                        break;
                                    case SettingsType.HUB:
                                        //Upload to hub
                                        logger.Info("Check - Upload to hub...");
                                        var hubSettings = GetSettings<HubSettings>(location);
                                        hubSettings.Type = SettingsType.HUB;
                                        var hubConfigs = JsonConvert.DeserializeObject<List<SerConnection>>(JsonConvert.SerializeObject(hubSettings?.Connections ?? new List<SerConnection>()));
                                        connectionManager.LoadConnections(hubConfigs, 1);
                                        var hubConnection = connectionManager.GetConnection(hubConfigs);
                                        if(hubConnection == null)
                                            throw new Exception("Could not create a connection to Qlik. (HUB)");
                                        if (hubSettings.Mode == DistributeMode.DELETEALLFIRST)
                                            execute.DeleteReportsFromHub(hubSettings, report, hubConnection, options.SessionUser);
                                        results.HubResults.AddRange(execute.UploadToHub(hubSettings, report, hubConnection, options.SessionUser));
                                        break;
                                    case SettingsType.MAIL:
                                        //Cache mail infos
                                        logger.Info("Check - Cache Mail...");
                                        var mailSettings = GetSettings<MailSettings>(location);
                                        mailSettings.Type = SettingsType.MAIL;
                                        mailSettings.MailReports.Add(report);
                                        mailList.Add(mailSettings);
                                        break;
                                    default:
                                        logger.Warn($"The delivery type of json {location} is unknown.");
                                        break;
                                }
                            }
                        }
                    }

                    //Send Mail
                    if (mailList.Count > 0)
                    {
                        logger.Info("Check - Send Mails...");
                        results.MailResults.AddRange(execute.SendMails(mailList));
                    }
                }

                connectionManager.MakeFree();
                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }
            catch (OperationCanceledException ex)
            {
                logger.Error(ex, "Distibute was canceled.");
                ErrorMessage = ex.Message;
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Can´t read job results.");
                ErrorMessage = ex.Message;
                return null;
            }
            finally
            {
                connectionManager.MakeFree();
            }
        }
    }
}