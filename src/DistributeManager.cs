﻿namespace Ser.Distribute
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
                        case "ftp":
                            return new T() { Type = SettingsType.FTP, Active = active };
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
            var results = new List<BaseResult>();
            var connectionManager = new ConnectionManager();

            try
            {
                var execute = new ExecuteManager();
                logger.Info("Read job results...");
                var jobIndex = 0;
                foreach (var jobResult in jobResults)
                {
                    //Check Cancel
                    options.CancelToken?.ThrowIfCancellationRequested();

                    jobIndex++;
                    if (jobResult.Status == TaskStatusInfo.ERROR || jobResult.Status == TaskStatusInfo.RETRYERROR)
                    {
                        results.Add(new ErrorResult()
                        {
                            Success = false,
                            ReportState = "ERROR",
                            JobName = $"Job{jobIndex}",
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
                            JobName = $"Job{jobIndex}",
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
                            JobName = $"Job{jobIndex}",
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
                            JobName = $"Job{jobIndex}",
                            Message = jobResult?.Exception?.FullMessage ?? "Unknown task status"
                        });
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
                        var distibuteActivationCount = 0;
                        foreach (var location in locations)
                        {
                            //Check Cancel
                            options.CancelToken?.ThrowIfCancellationRequested();

                            var settings = GetSettings<BaseDeliverySettings>(location, true);
                            if (settings.Active ?? true)
                            {
                                distibuteActivationCount++;
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
                                        results.AddRange(execute.CopyFile(fileSettings, report, fileConnection, jobResult, jobIndex));
                                        break;
                                    case SettingsType.FTP:
                                        //Upload to FTP or FTPS
                                        logger.Info("Check - Upload to FTP...");
                                        var ftpSettings = GetSettings<FTPSettings>(location);
                                        ftpSettings.Type = SettingsType.FTP;
                                        results.AddRange(execute.FtpUpload(ftpSettings, report, jobResult, jobIndex));
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
                                        if (hubSettings.Mode == DistributeMode.DELETEALLFIRST)
                                            execute.DeleteReportsFromHub(hubSettings, report, hubConnection, options.SessionUser);
                                        results.AddRange(execute.UploadToHub(hubSettings, report, hubConnection, options.SessionUser, jobResult, jobIndex));
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

                        if (distibuteActivationCount == 0)
                        {
                            results.Add(new DistibutionResult()
                            {
                                Success = true,
                                Message = "No delivery type was selected for the report.",
                                JobName = $"Job{jobIndex}",
                                ReportState = jobResult.Status.ToString().ToUpperInvariant()
                            });
                        }
                    }

                    //Send Mail
                    if (mailList.Count > 0)
                    {
                        logger.Info("Check - Send Mails...");
                        results.AddRange(execute.SendMails(mailList, options, jobResult, jobIndex));
                    }
                }

                execute.CleanUp();
                connectionManager.MakeFree();

                //Check Cancel
                options.CancelToken?.ThrowIfCancellationRequested();

                results = results.OrderBy(r => r.GetType().Name).ToList();
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