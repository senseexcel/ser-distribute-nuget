namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Ser.Api;
    using Q2g.HelperQlik;
    using System.Threading;
    #endregion

    public class Distribute
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
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
                return default(T);
            }
        }

        public string Run(string resultFolder, string privateKeyPath = null)
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
                    var fileDataList = new List<JobResultFileData>();
                    foreach (var report in result.Reports)
                    {
                        foreach (var path in report.Paths)
                        {
                            var data = File.ReadAllBytes(path);
                            var fileData = new JobResultFileData()
                            {
                                Filename = Path.GetFileName(path),
                                Data = data
                            };
                            fileDataList.Add(fileData);
                        }
                    }
                    result.SetData(fileDataList);
                    jobResults.Add(result);
                }
                return Run(jobResults, privateKeyPath);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Can´t read job results from path.");
                return null;
            }
        }

        public string Run(List<JobResult> jobResults, string privateKeyPath = null, CancellationToken? token = null)
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
                    token?.ThrowIfCancellationRequested();

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
                        token?.ThrowIfCancellationRequested();

                        var distribute = report?.Distribute ?? null;
                        var resolver = new CryptoResolver(privateKeyPath);
                        distribute = resolver.Resolve(distribute);
                        var locations = distribute?.Children().ToList() ?? new List<JToken>();
                        foreach (var location in locations)
                        {
                            //Check Cancel
                            token?.ThrowIfCancellationRequested();

                            var settings = GetSettings<BaseDeliverySettings>(location, true);
                            if (settings.Active ?? true)
                            {
                                switch (settings.Type)
                                {
                                    case SettingsType.FILE:
                                        //Copy reports
                                        logger.Info("Check - Copy Files...");
                                        var fileSettings = GetSettings<FileSettings>(location);
                                        var fileConfigs = JsonConvert.DeserializeObject<List<ConnectionConfig>>(JsonConvert.SerializeObject(fileSettings?.Connections ?? new List<SerConnection>()));
                                        var fileConnection = connectionManager.GetConnection(fileConfigs);
                                        results.FileResults.AddRange(execute.CopyFile(fileSettings, jobResult.GetData(), report, fileConnection));
                                        break;
                                    case SettingsType.HUB:
                                        //Upload to hub
                                        logger.Info("Check - Upload to hub...");
                                        var hubSettings = GetSettings<HubSettings>(location);
                                        var hubConfigs = JsonConvert.DeserializeObject<List<ConnectionConfig>>(JsonConvert.SerializeObject(hubSettings?.Connections ?? new List<SerConnection>()));
                                        connectionManager.LoadConnections(hubConfigs, 1);
                                        var hubConnection = connectionManager.GetConnection(hubConfigs);
                                        var task = execute.UploadToHub(hubSettings, jobResult.GetData(), report, hubConnection);
                                        if (task != null)
                                            uploadTasks.Add(task);
                                        break;
                                    case SettingsType.MAIL:
                                        //Cache mail infos
                                        logger.Info("Check - Cache Mail...");
                                        var mailSettings = GetSettings<MailSettings>(location);
                                        mailSettings.SetData(jobResult.GetData());
                                        mailSettings.ReportName = report.Name;
                                        mailSettings.Paths = report?.Paths;
                                        mailList.Add(mailSettings);
                                        break;
                                    default:
                                        logger.Warn($"The delivery type of json {location} is unknown.");
                                        break;
                                }
                            }
                        }
                    }

                    //Wait for all upload tasks
                    Task.WaitAll(uploadTasks.ToArray());

                    //Evaluation hub results
                    foreach (var uploadTask in uploadTasks)
                        results.HubResults.Add(uploadTask.Result);

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
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Can´t read job results.");
                return null;
            }
            finally
            {
                connectionManager.MakeFree();
            }
        }
    }
}