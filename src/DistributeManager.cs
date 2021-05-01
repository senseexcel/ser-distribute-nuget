namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Q2g.HelperQlik;
    using Ser.Distribute.Actions;
    using Ser.Distribute.Messenger;
    using Ser.Api;
    using Ser.Api.Model;
    using Ser.Distribute.Settings;
    using System.Threading;
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
        private static T GetSettings<T>(JToken json, bool typeOnly = false) where T : DistributeSettings, new()
        {
            try
            {
                if (typeOnly)
                {
                    var active = json?.Children()["active"]?.ToList()?.FirstOrDefault()?.ToObject<bool>() ?? false;
                    var jProperty = json as JProperty;
                    if (jProperty?.Name?.StartsWith("mail") ?? false)
                        return new T() { Type = SettingsType.MAIL, Active = active };
                    else if (jProperty?.Name?.StartsWith("hub") ?? false)
                        return new T() { Type = SettingsType.HUB, Active = active };
                    else if (jProperty?.Name?.StartsWith("file") ?? false)
                        return new T() { Type = SettingsType.FILE, Active = active };
                    else if (jProperty?.Name?.StartsWith("ftp") ?? false)
                        return new T() { Type = SettingsType.FTP, Active = active };
                    else if (jProperty?.Name?.StartsWith("messenger") ?? false)
                        return new T() { Type = SettingsType.MESSENGER, Active = active };
                    else
                        return null;
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
        #endregion

        #region Public Methods
        public string Run(List<JobResult> jobResults, CancellationToken? token = null)
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
                    token?.ThrowIfCancellationRequested();

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
                    var mailAction = new MailAction(jobResult);
                    var messengerList = new List<BaseMessenger>();
                    foreach (var report in jobResult.Reports)
                    {
                        //Check Cancel
                        token?.ThrowIfCancellationRequested();

                        fileSystemAction.Results.Clear();
                        ftpAction.Results.Clear();
                        hubAction.Results.Clear();
                        mailAction.Results.Clear();

                        var distribute = report?.Distribute ?? null;
                        var locations = distribute?.Children().ToList() ?? new List<JToken>();
                        var distibuteActivationCount = 0;
                        foreach (var location in locations)
                        {
                            //Check Cancel
                            token?.ThrowIfCancellationRequested();

                            var settings = GetSettings<DistributeSettings>(location, true);
                            if (settings.Active)
                            {
                                distibuteActivationCount++;
                                switch (settings.Type)
                                {
                                    case SettingsType.MESSENGER:
                                        var messengerSettings = GetSettings<MessengerSettings>(location);
                                        messengerSettings.Type = SettingsType.MESSENGER;
                                        switch (messengerSettings.Messenger)
                                        {
                                            case MessengerType.MICROSOFTTEAMS:
                                                var msTeams = new MicrosoftTeams(messengerSettings, jobResult);
                                                messengerList.Add(msTeams);
                                                break;
                                            case MessengerType.SLACK:
                                                var slack = new Slack(messengerSettings, jobResult);
                                                messengerList.Add(slack);
                                                break;
                                            default:
                                                throw new Exception($"Unkown messenger '{messengerSettings.Messenger}'.");
                                        }
                                        break;
                                    case SettingsType.FILE:
                                        //Copy reports
                                        logger.Info("Check - Copy Files...");
                                        var fileSettings = GetSettings<FileSettings>(location);
                                        fileSettings.Type = SettingsType.FILE;
                                        var fileConfigs = JsonConvert.DeserializeObject<List<SerConnection>>(JsonConvert.SerializeObject(fileSettings?.Connections ?? new List<SerConnection>()));
                                        var fileConnection = connectionManager.GetConnection(fileConfigs, token);
                                        if (fileConnection == null)
                                            throw new Exception("Could not create a connection to Qlik. (FILE)");
                                        fileSystemAction.CopyFile(report, fileSettings, fileConnection);
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
                                        var hubConnection = connectionManager.GetConnection(hubConfigs, token);
                                        if (hubConnection == null)
                                            throw new Exception("Could not create a connection to Qlik. (HUB)");
                                        hubAction.UploadToHub(report, hubSettings, hubConnection);
                                        results.AddRange(hubAction.Results);
                                        break;
                                    case SettingsType.MAIL:
                                        //Cache mail infos
                                        logger.Info("Check - Cache Mail...");
                                        var mailSettings = GetSettings<MailSettings>(location);
                                        mailSettings.Type = SettingsType.MAIL;
                                        mailAction.MailSettings.Add(mailSettings);
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

                    if (mailAction.MailSettings.Count > 0)
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
                        foreach (var messengner in messengerList)
                        {
                            logger.Debug($"Send message with '{messengner?.Settings?.Messenger}'...");
                            results.Add(messengner.SendMessage(results));
                        }
                    }
                }

                //Make all Sockets free
                connectionManager.MakeFree();

                //Check Cancel
                token?.ThrowIfCancellationRequested();

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