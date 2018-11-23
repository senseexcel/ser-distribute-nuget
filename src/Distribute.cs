﻿namespace Ser.Distribute
{
    #region Usings
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
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
            var results = new DistributeResults();

            try
            {
                var execute = new ExecuteManager();
                logger.Info("Read json result files...");
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
                    if (result.Status != TaskStatusInfo.SUCCESS)
                    {
                        logger.Warn($"The result \"{result.Status}\" of the report {jsonPath} is not correct. The report is ignored.");
                        continue;
                    }

                    var mailList = new List<MailSettings>();
                    var uploadTasks = new List<Task<HubResult>>();
                    foreach (var report in result.Reports)
                    {
                        var distribute = report?.Distribute ?? null;
                        var resolver = new CryptoResolver(privateKeyPath);
                        distribute = resolver.Resolve(distribute);

                        var locations = distribute?.Children().ToList() ?? new List<JToken>();
                        foreach (var location in locations)
                        {
                            var settings = GetSettings<BaseDeliverySettings>(location, true);
                            if (settings.Active ?? true)
                            {
                                switch (settings.Type)
                                {
                                    case SettingsType.FILE:
                                        //copy reports
                                        logger.Info("Check - Copy Files...");
                                        var fileSettings = GetSettings<FileSettings>(location);
                                        results.FileResults.AddRange(execute.CopyFile(fileSettings, report.Paths, report.Name));
                                        break;
                                    case SettingsType.HUB:
                                        //upload to hub
                                        logger.Info("Check - Upload to hub...");
                                        var hubSettings = GetSettings<HubSettings>(location);
                                        var task = execute.UploadToHub(hubSettings, report.Paths, report.Name);
                                        if (task != null)
                                            uploadTasks.Add(task);
                                        break;
                                    case SettingsType.MAIL:
                                        //cache mail infos
                                        logger.Info("Check - Cache Mail...");
                                        var mailSettings = GetSettings<MailSettings>(location);
                                        mailSettings.Paths = report.Paths;
                                        mailSettings.ReportName = report.Name;
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
                return JsonConvert.SerializeObject(results, Formatting.Indented);
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                return null;
            }
        }
    }
}