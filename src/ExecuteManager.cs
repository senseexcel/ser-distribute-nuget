namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Mail;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Net;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Markdig;
    using Ser.Api;
    using Q2g.HelperQrs;
    using Q2g.HelperQlik;
    #endregion

    public class ExecuteManager
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        public bool deleteFirst;
        private Dictionary<string, string> pathMapper;
        #endregion

        #region Constructor
        public ExecuteManager()
        {
            deleteFirst = false;
            pathMapper = new Dictionary<string, string>();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            if (ServicePointManager.ServerCertificateValidationCallback == null)
                ServicePointManager.ServerCertificateValidationCallback += ValidationCallback.ValidateRemoteCertificate;
        }
        #endregion

        #region Private Methods
        private string NormalizeLibPath(string path, FileSettings settings, Q2g.HelperQlik.Connection fileConnection)
        {
            try
            {
                var result = UriUtils.NormalizeUri(path);
                var libUri = result.Item1;

                var connections = fileConnection?.CurrentApp?.GetConnectionsAsync().Result ?? null;
                if (connections != null)
                {
                    var libResult = connections.FirstOrDefault(n => n.qName.ToLowerInvariant() == result.Item2) ?? null;
                    if (libResult == null)
                    {
                        logger.Error($"No data connection with name {result.Item2} found.");
                        return null;
                    }

                    var libPath = libResult.qConnectionString.ToString();
                    var resultPath = Path.Combine(libPath, libUri.LocalPath.Replace("/", "\\").Trim().Trim('\\'));
                    return resultPath;
                }
                else
                    logger.Error("No data connections found.");
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The lib path could not resolve.");
                return null;
            }
        }

        private HubInfo GetSharedContentFromUser(QlikQrsHub hub, string name, DomainUser hubUser)
        {
            var hubRequest = new HubSelectRequest()
            {
                Filter = HubSelectRequest.GetNameFilter(name),
            };
            var sharedContentInfos = hub.GetSharedContentAsync(hubRequest)?.Result;
            if (sharedContentInfos == null)
                return null;

            if (hubUser == null)
                return sharedContentInfos.FirstOrDefault() ?? null;

            foreach (var sharedContent in sharedContentInfos)
            {
                if (sharedContent.Owner.ToString() == hubUser.ToString())
                {
                    return sharedContent;
                }
            }

            return null;
        }

        private JobResultFileData GetFileData(List<JobResultFileData> fileDataList, string reportPath, Guid taskId)
        {
            return fileDataList.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath) && f.TaskId == taskId);
        }

        private string GetContentName(string reportName, JobResultFileData fileData)
        {
            return $"{Path.GetFileNameWithoutExtension(reportName)} ({Path.GetExtension(fileData.Filename).TrimStart('.').ToUpperInvariant()})";
        }
        #endregion

        public List<FileResult> CopyFile(FileSettings settings, List<JobResultFileData> fileDataList, Report report, Q2g.HelperQlik.Connection fileConnection)
        {
            var fileResults = new List<FileResult>();
            var reportName = report?.Name ?? null;
            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report filename is empty.");

                var target = settings?.Target?.Trim() ?? null;
                if (target == null)
                {
                    var message = $"No target file path for report {reportName} found.";
                    logger.Error(message);
                    fileResults.Add(new FileResult() { Success = false, Message = message, ReportName = reportName });
                    return fileResults;
                }

                if (!target.ToLowerInvariant().StartsWith("lib://"))
                {
                    var message = $"Target value \"{target}\" is not a lib:// folder.";
                    logger.Error(message);
                    fileResults.Add(new FileResult() { Success = false, Message = message, ReportName = reportName });
                    return fileResults;
                }

                string targetPath = String.Empty;
                if (pathMapper.ContainsKey(target))
                    targetPath = pathMapper[target];
                else
                {
                    targetPath = NormalizeLibPath(target, settings, fileConnection);
                    if (targetPath == null)
                        throw new Exception("The lib path could not be resolved.");
                    pathMapper.Add(target, targetPath);
                }

                logger.Info($"Resolve target path: \"{targetPath}\".");
                var fileCount = 0;
                foreach (var reportPath in report.Paths)
                {
                    if (report.Paths.Count > 1)
                        fileCount++;
                    var fileData = fileDataList.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var targetFile = Path.Combine(targetPath, $"{reportName}{Path.GetExtension(reportPath)}");
                    if (fileCount > 0)
                        targetFile = Path.Combine(targetPath, $"{reportName}_{fileCount}{Path.GetExtension(reportPath)}");
                    logger.Debug($"copy mode {settings.Mode}");
                    switch (settings.Mode)
                    {
                        case DistributeMode.OVERRIDE:
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(targetFile, fileData.Data);
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            if (File.Exists(targetFile))
                                File.Delete(targetFile);
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(targetFile, fileData.Data);
                            break;
                        case DistributeMode.CREATEONLY:
                            if (File.Exists(targetFile))
                                throw new Exception($"The file {targetFile} does not exist.");
                            File.WriteAllBytes(targetFile, fileData.Data);
                            break;
                        default:
                            throw new Exception($"Unkown distribute mode {settings.Mode}");
                    }
                    logger.Info($"file {targetFile} was copied - Mode {settings.Mode}");
                    fileResults.Add(new FileResult() { Success = true, ReportName = reportName, Message = "File copy successful.", CopyPath = targetFile });
                }
                return fileResults;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The copying process could not be execute.");
                fileResults.Add(new FileResult() { Success = false, Message = ex.Message, ReportName = reportName });
                return fileResults;
            }
            finally
            {
                fileConnection.IsFree = true;
            }
        }

        public void DeleteReportsFromHub(HubSettings settings, JobResult jobResult, List<JobResultFileData> fileDataList, Q2g.HelperQlik.Connection hubConnection)
        {
            try
            {
                if (deleteFirst)
                {
                    settings.Mode = DistributeMode.CREATEONLY;
                    return;
                }

                var hubUser = new DomainUser(settings.Owner);
                var hubUri = Q2g.HelperQlik.Connection.BuildQrsUri(hubConnection.ConnectUri, hubConnection.Config.ServerUri);
                var hub = new QlikQrsHub(hubUri, hubConnection.ConnectCookie);
                var sharedContentInfos = hub.GetSharedContentAsync(new HubSelectRequest())?.Result;
                if (sharedContentInfos == null)
                    logger.Debug("No shared content found.");

                foreach (var report in jobResult.Reports)
                {
                    foreach (var reportPath in report.Paths)
                    {
                        var fileData = GetFileData(fileDataList, reportPath, jobResult.TaskId);
                        var contentName = GetContentName(report?.Name ?? null, fileData);
                        var sharedContentList = sharedContentInfos.Where(s => s.Name == contentName).ToList();
                        foreach (var sharedContent in sharedContentList)
                        {
                            var serMetaType = sharedContent.MetaData.Where(m => m.Key == "ser-type" && m.Value == "report").SingleOrDefault() ?? null;
                            if (sharedContent.MetaData == null)
                                serMetaType = new MetaData();

                            if (serMetaType != null && sharedContent.Owner.ToString() == hubUser.ToString())
                                hub.DeleteSharedContentAsync(new HubDeleteRequest() { Id = sharedContent.Id.Value }).Wait();
                        }
                    }
                }

                deleteFirst = true;
                settings.Mode = DistributeMode.CREATEONLY;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Reports could not delete");
            }
        }

        public Task<HubResult> UploadToHub(HubSettings settings, List<JobResultFileData> fileDataList, Report report, Q2g.HelperQlik.Connection hubConnection, Guid taskId)
        {
            var hubResult = new HubResult();
            var reportName = report?.Name ?? null;

            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report filename is empty.");

                var hubUri = Q2g.HelperQlik.Connection.BuildQrsUri(hubConnection.ConnectUri, hubConnection.Config.ServerUri);
                var hub = new QlikQrsHub(hubUri, hubConnection.ConnectCookie);
                foreach (var reportPath in report.Paths)
                {
                    var fileData = GetFileData(fileDataList, reportPath, taskId);
                    var contentName = GetContentName(reportName, fileData);

                    if (settings.Mode == DistributeMode.OVERRIDE ||
                        settings.Mode == DistributeMode.CREATEONLY)
                    {
                        return Task.Run<HubResult>(() =>
                        {
                            var uploadResult = new HubResult()
                            {
                                ReportName = reportName,
                            };

                            try
                            {
                                HubInfo hubInfo = null;
                                Guid? hubUserId = null;
                                DomainUser hubUser = null;
                                if (settings.Owner != null)
                                {
                                    hubUser = new DomainUser(settings.Owner);
                                    var filter = $"userId eq '{hubUser.UserId}' and userDirectory eq '{hubUser.UserDirectory}'";
                                    var result = hub.SendRequestAsync("user", HttpMethod.Get, null, filter).Result;
                                    if (result == null)
                                        throw new Exception($"Qlik user {settings.Owner} with qrs not found or session not connected.");
                                    var userObject = JArray.Parse(result);
                                    if (userObject.Count > 1)
                                        throw new Exception($"Too many User found. {result}");
                                    else if (userObject.Count == 1)
                                        hubUserId = new Guid(userObject.First()["id"].ToString());
                                }
                                var sharedContent = GetSharedContentFromUser(hub, contentName, hubUser);
                                if (sharedContent == null)
                                {
                                    var createRequest = new HubCreateRequest()
                                    {
                                        Name = contentName,
                                        ReportType = settings.SharedContentType,
                                        Description = "Created by Sense Excel Reporting",
                                        Tags = new List<Tag>() { new Tag() 
                                            { 
                                                 Name = "SER",
                                                 CreatedDate = DateTime.Now,
                                                 ModifiedDate = DateTime.Now
                                            } 
                                        },
                                        Data = new ContentData()
                                        {
                                            ContentType = $"application/{Path.GetExtension(fileData.Filename).Trim('.')}",
                                            ExternalPath = Path.GetFileName(fileData.Filename),
                                            FileData = fileData.Data,
                                        }
                                    };
                                    hubInfo = hub.CreateSharedContentAsync(createRequest).Result;
                                }
                                else
                                {
                                    if (settings.Mode == DistributeMode.OVERRIDE)
                                    {
                                        var tag = sharedContent?.Tags?.FirstOrDefault(t => t.Name == "SER") ?? null;
                                        if (tag != null)
                                        {
                                            tag.CreatedDate = DateTime.Now;
                                            tag.ModifiedDate = DateTime.Now;
                                        }
                                        var updateRequest = new HubUpdateRequest()
                                        {
                                            Info = sharedContent,
                                            Data = new ContentData()
                                            {
                                                ContentType = $"application/{Path.GetExtension(fileData.Filename).Trim('.')}",
                                                ExternalPath = Path.GetFileName(fileData.Filename),
                                                FileData = fileData.Data,
                                            }
                                        };
                                        hubInfo = hub.UpdateSharedContentAsync(updateRequest).Result;
                                    }
                                    else
                                    {
                                        throw new Exception($"The shared content {contentName} already exist.");
                                    }
                                }

                                if (hubUserId != null)
                                {
                                    //change shared content owner
                                    var newHubInfo = new HubInfo()
                                    {
                                        Id = hubInfo.Id,
                                        Type = settings.SharedContentType,
                                        Owner = new Owner()
                                        {
                                            Id = hubUserId.ToString(),
                                            UserId = hubUser.UserId,
                                            UserDirectory = hubUser.UserDirectory,
                                            Name = hubUser.UserId,
                                        }
                                    };

                                    var changeRequest = new HubUpdateRequest()
                                    {
                                        Info = newHubInfo,
                                    };
                                    hub.UpdateSharedContentAsync(changeRequest).Wait();
                                }

                                // get fresh shared content infos
                                var filename = Path.GetFileName(fileData.Filename);
                                hubInfo = GetSharedContentFromUser(hub, contentName, hubUser);
                                uploadResult.Link = hubInfo?.References?.FirstOrDefault(r => r.ExternalPath.ToLowerInvariant().Contains($"/{filename}"))?.ExternalPath ?? null;
                                uploadResult.Message = $"Upload {contentName} successful.";
                                uploadResult.Success = true;
                                return uploadResult;
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, "The process could not be upload to the hub.");
                                uploadResult.Success = false;
                                uploadResult.Message = ex.Message;
                                return uploadResult;
                            }
                            finally
                            {
                                hubConnection.IsFree = true;
                            }
                        });
                    }
                    else
                    {
                        throw new Exception($"Unknown hub mode {settings.Mode}");
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The process could not be upload to the hub.");
                hubResult.Success = false;
                hubResult.Message = ex.Message;
                return Task.FromResult(hubResult);
            }
        }

        public List<MailResult> SendMails(List<MailSettings> settingsList)
        {
            SmtpClient client = null;
            var mailMessage = new MailMessage();
            var mailResults = new List<MailResult>();
            var mailResult = new MailResult();

            try
            {
                var mailList = new List<EMailReport>();
                foreach (var mailSettings in settingsList)
                {
                    var fileDataList = mailSettings.GetData();
                    foreach (var path in mailSettings.Paths)
                    {
                        var fileData = fileDataList.FirstOrDefault(f => Path.GetFileName(path) == f.Filename);
                        var result = mailList.SingleOrDefault(m => m.Settings.ToString() == mailSettings.ToString());
                        if (result == null)
                        {
                            logger.Debug("Add report to mail");
                            var mailReport = new EMailReport(mailSettings, mailSettings.MailServer, mailSettings.ToString());
                            mailReport.AddReport(fileData, mailSettings.ReportName);
                            mailList.Add(mailReport);
                        }
                        else
                        {
                            logger.Debug("Merge report to mail");
                            result.AddReport(fileData, mailSettings.ReportName);
                        }
                    }
                }

                //send merged mail infos
                logger.Debug($"{mailList.Count} Mails to send.");
                foreach (var report in mailList)
                {
                    mailResult = new MailResult();
                    mailMessage = new MailMessage();
                    mailResult.ReportName = report?.Settings?.ReportName ?? null;
                    var toAddresses = report.Settings.To?.Split(';') ?? new string[0];
                    var ccAddresses = report.Settings.Cc?.Split(';') ?? new string[0];
                    var bccAddresses = report.Settings.Bcc?.Split(';') ?? new string[0];
                    mailMessage.Subject = report.Settings.Subject;
                    var msgBody = report.Settings.Message.Trim();
                    switch (report.Settings.MailType)
                    {
                        case EMailType.TEXT:
                            msgBody = msgBody.Replace("{n}", "\r\n");
                            break;
                        case EMailType.HTML:
                            mailMessage.IsBodyHtml = true;
                            break;
                        case EMailType.MARKDOWN:
                            mailMessage.IsBodyHtml = true;
                            msgBody = Markdown.ToHtml(msgBody);
                            break;
                        default:
                            throw new Exception($"Unknown mail type {report.Settings.MailType}");
                    }
                    mailMessage.Body = msgBody;
                    mailMessage.From = new MailAddress(report.ServerSettings.From);
                    if (report.Settings.SendAttachment)
                    {
                        foreach (var attach in report.ReportPaths)
                        {
                            mailMessage.Attachments.Add(attach);
                        }
                    }

                    foreach (var address in toAddresses)
                    {
                        if (!String.IsNullOrEmpty(address))
                            mailMessage.To.Add(address);
                    }

                    foreach (var address in ccAddresses)
                    {
                        if (!String.IsNullOrEmpty(address))
                            mailMessage.CC.Add(address);
                    }

                    foreach (var address in bccAddresses)
                    {
                        if (!String.IsNullOrEmpty(address))
                            mailMessage.Bcc.Add(address);
                    }

                    client = new SmtpClient(report.ServerSettings.Host, report.ServerSettings.Port)
                    {
                        Credentials = new NetworkCredential(report.ServerSettings.Username, report.ServerSettings.Password),
                    };
                    logger.Debug("Send mail package...");
                    client.EnableSsl = report.ServerSettings.UseSsl;
                    client.Send(mailMessage);

                    mailMessage.Dispose();
                    client.Dispose();
                    mailResult.Success = true;
                    mailResult.Message = "Mail sent successful.";
                    mailResults.Add(mailResult);
                }
                return mailResults;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The reports could not be sent as mail.");
                if (mailMessage != null)
                    mailMessage.Dispose();
                if (client != null)
                    client.Dispose();
                mailResult.Success = false;
                mailResult.Message = ex.Message;
                mailResults.Add(mailResult);
                return mailResults;
            }
        }
    }
}