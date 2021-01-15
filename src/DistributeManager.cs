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
    using Ser.Distribute.Actions;
    using System.Text;
    using Ser.Distribute.Messenger;
    #endregion

    public class DistributeManager
    {
        #region Logger
        private readonly static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        public string ErrorMessage { get; private set; }
        #endregion

        #region Private Methods
        private static T GetSettings<T>(JToken json, bool typeOnly = false) where T : DistibuteSettings, new()
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
                        case "ftp":
                            return new T() { Type = SettingsType.FTP, Active = active };
                        case "messenger":
                            return new T() { Type = SettingsType.MESSENGER, Active = active };
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

        private static List<BaseResult> NormalizeReportState(List<BaseResult> results)
        {
            var groupedResults = results.GroupBy(r => r.TaskName).Select(grp => grp.ToList()).ToList();
            foreach (var groupedResult in groupedResults)
            {
                foreach (var jobResult in groupedResult)
                {
                    var state = jobResult?.ReportState ?? null;
                    if (state?.ToLowerInvariant() == "error" && groupedResult.Count > 1)
                    {
                        foreach (var jobInternalResult in groupedResult)
                            jobInternalResult.ReportState = "ERROR";
                        break;
                    }
                }
            }
            return groupedResults.SelectMany(r => r).ToList();
        }

        private static List<MessengerResult> SendBotMessages(List<BaseResult> distibuteResults, List<MessengerSettings> messengerList)
        {
            var results = new List<MessengerResult>();
            foreach (var messenger in messengerList)
            {
                switch (messenger.Messenger)
                {
                    case MessengerType.MICROSOFTTEAMS:
                        var msTeams = new MicrosoftTeams(messenger);
                        results.Add(msTeams.SendMessage(distibuteResults));
                        break;
                    case MessengerType.SLACK:
                        var slack = new Slack(messenger);
                        results.Add(slack.SendMessage(distibuteResults));
                        break;
                    default:
                        throw new Exception($"Unkown messenger '{messenger.Messenger}'.");
                }
            }
            return results;
        }
        #endregion

        #region Public Methods
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
            var results = new List<BaseResult>();
            var connectionManager = new ConnectionManager();

            try
            {
                logger.Info("Read job results...");
                var taskIndex = 0;
                foreach (var jobResult in jobResults)
                {
                    //Check Cancel
                    options.CancelToken?.ThrowIfCancellationRequested();

                    taskIndex++;
                    jobResult.TaskName = $"Task {taskIndex}";
                    if (jobResult.Status == TaskStatusInfo.ERROR || jobResult.Status == TaskStatusInfo.RETRYERROR)
                    {
                        results.Add(new ErrorResult()
                        {
                            Success = false,
                            ReportState = "ERROR",
                            TaskName = jobResult.TaskName,
                            Message = jobResult?.Exception?.FullMessage ?? "Unknown error"
                        });
                        continue;
                    }
                    else if (jobResult.Status == TaskStatusInfo.INACTIVE)
                    {
                        results.Add(new ErrorResult()
                        {
                            Success = true,
                            ReportState = "INACTIVE",
                            TaskName = jobResult.TaskName,
                            Message = jobResult?.Exception?.FullMessage ?? "inactive task"
                        });
                        continue;
                    }
                    else if (jobResult.Status == TaskStatusInfo.ABORT)
                    {
                        results.Add(new ErrorResult()
                        {
                            Success = true,
                            ReportState = "ABORT",
                            TaskName = jobResult.TaskName,
                            Message = jobResult?.Exception?.FullMessage ?? "Task was canceled"
                        });
                        continue;
                    }
                    else if (jobResult.Status == TaskStatusInfo.WARNING)
                    {
                        logger.Info("The report status includes a warning, but it is delivered...");
                    }
                    else if (jobResult.Status == TaskStatusInfo.SUCCESS)
                    {
                        logger.Info($"The report was successfully created and is now being delivered...");
                    }
                    else
                    {
                        logger.Error($"The report has a unknown status '{jobResult.Status}'...");
                        results.Add(new ErrorResult()
                        {
                            Success = false,
                            ReportState = jobResult.Status.ToString().ToUpperInvariant(),
                            TaskName = jobResult.TaskName,
                            Message = jobResult?.Exception?.FullMessage ?? "Unknown task status"
                        });
                        continue;
                    }

                    var fileSystemAction = new FileSystemAction(jobResult);
                    var ftpAction = new FtpAction(jobResult);
                    var hubAction = new HubAction(jobResult);
                    var mailAction = new MailAction(jobResult, options.PrivateKeyPath);
                    var messengerList = new List<MessengerSettings>();
                    foreach (var report in jobResult.Reports)
                    {
                        //Check Cancel
                        options.CancelToken?.ThrowIfCancellationRequested();

                        fileSystemAction.Results.Clear();
                        ftpAction.Results.Clear();
                        hubAction.Results.Clear();
                        mailAction.Results.Clear();

                        var distribute = report?.Distribute ?? null;
                        var resolver = new CryptoResolver(options.PrivateKeyPath);
                        distribute = resolver.Resolve(distribute);
                        var locations = distribute?.Children().ToList() ?? new List<JToken>();
                        var distibuteActivationCount = 0;
                        foreach (var location in locations)
                        {
                            //Check Cancel
                            options.CancelToken?.ThrowIfCancellationRequested();

                            var settings = GetSettings<DistibuteSettings>(location, true);
                            if (settings.Active ?? true)
                            {
                                distibuteActivationCount++;
                                switch (settings.Type)
                                {
                                    case SettingsType.MESSENGER:
                                        var messengerSettings = GetSettings<MessengerSettings>(location);
                                        messengerSettings.Type = SettingsType.MESSENGER;
                                        messengerSettings.JobResult = jobResult;
                                        messengerList.Add(messengerSettings);
                                        break;
                                    case SettingsType.FILE:
                                        //Copy reports
                                        logger.Info("Check - Copy Files...");
                                        var fileSettings = GetSettings<FileSettings>(location);
                                        fileSettings.Type = SettingsType.FILE;
                                        var fileConfigs = JsonConvert.DeserializeObject<List<SerConnection>>(JsonConvert.SerializeObject(fileSettings?.Connections ?? new List<SerConnection>()));
                                        var fileConnection = connectionManager.GetConnection(fileConfigs);
                                        if (fileConnection == null)
                                            throw new Exception("Could not create a connection to Qlik. (FILE)");
                                        fileSettings.SocketConnection = fileConnection;
                                        fileSystemAction.CopyFile(report, fileSettings);
                                        results.AddRange(fileSystemAction.Results);
                                        break;
                                    case SettingsType.FTP:
                                        //Upload to FTP or FTPS
                                        logger.Info("Check - Upload to FTP...");
                                        var ftpSettings = GetSettings<FTPSettings>(location);
                                        ftpSettings.Type = SettingsType.FTP;
                                        ftpAction.FtpUpload(report, ftpSettings);
                                        results.AddRange(ftpAction.Results);
                                        break;
                                    case SettingsType.HUB:
                                        //Upload to hub
                                        logger.Info("Check - Upload to hub...");
                                        var hubSettings = GetSettings<HubSettings>(location);
                                        hubSettings.Type = SettingsType.HUB;
                                        var hubConfigs = JsonConvert.DeserializeObject<List<SerConnection>>(JsonConvert.SerializeObject(hubSettings?.Connections ?? new List<SerConnection>()));
                                        connectionManager.LoadConnections(hubConfigs, 1);
                                        var hubConnection = connectionManager.GetConnection(hubConfigs);
                                        if (hubConnection == null)
                                            throw new Exception("Could not create a connection to Qlik. (HUB)");
                                        hubSettings.SocketConnection = hubConnection;
                                        hubSettings.SessionUser = options.SessionUser;
                                        hubAction.UploadToHub(report, hubSettings);
                                        results.AddRange(hubAction.Results);
                                        break;
                                    case SettingsType.MAIL:
                                        //Cache mail infos
                                        logger.Info("Check - Cache Mail...");
                                        var mailSettings = GetSettings<MailSettings>(location);
                                        mailAction.AddMailSettings(mailSettings, report);
                                        break;
                                    default:
                                        logger.Warn($"The delivery type of json {location} is unknown.");
                                        break;
                                }
                            }
                        }

                        if (distibuteActivationCount == 0)
                        {
                            results.Add(new DistibutionResult()
                            {
                                Success = true,
                                Message = "No delivery type was selected for the report.",
                                TaskName = jobResult.TaskName,
                                ReportState = jobResult.Status.ToString().ToUpperInvariant()
                            });
                        }
                    }

                    if (mailAction.MailSettingsList.Count > 0)
                    {
                        //Send Mails
                        logger.Info("Send mails...");
                        mailAction.SendMails();
                        results.AddRange(mailAction.Results);
                    }

                    //Send Messanger messages
                    if (messengerList.Count > 0)
                    {
                        logger.Info("Send report infos with messenger...");
                        results.AddRange(SendBotMessages(results, messengerList));
                    }
                }

                //Make all Sockets free
                connectionManager.MakeFree();

                //Check Cancel
                options.CancelToken?.ThrowIfCancellationRequested();

                results = results.OrderBy(r => r.TaskName).ToList();
                results = NormalizeReportState(results);
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
        #endregion
    }
}