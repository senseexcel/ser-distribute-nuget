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
        private List<string> hubDeleteAll;
        private Dictionary<string, string> pathMapper;
        #endregion

        #region Constructor
        public ExecuteManager()
        {
            hubDeleteAll = new List<string>();
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
        #endregion

        public List<FileResult> CopyFile(FileSettings settings, List<JobResultFileData> fileDataList, Report report, Q2g.HelperQlik.Connection fileConnection)
        {
            var fileResults = new List<FileResult>();
            var reportName = report?.Name ?? null;
            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report filename is empty.");

                var target = settings.Target?.ToLowerInvariant()?.Trim() ?? null;
                if (target == null)
                {
                    var message = $"No target file path for report {reportName} found.";
                    logger.Error(message);
                    fileResults.Add(new FileResult() { Success = false, Message = message, ReportName = reportName });
                    return fileResults;
                }

                if (!target.StartsWith("lib://"))
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
                        throw new Exception("The could not resolved.");
                    pathMapper.Add(target, targetPath);
                }

                logger.Info($"Resolve target path: \"{targetPath}\".");
                foreach (var fileData in fileDataList)
                {
                    var targetFile = Path.Combine(targetPath, $"{fileData.Filename}");
                    logger.Debug($"copy mode {settings.Mode}");
                    switch (settings.Mode)
                    {
                        case DistributeMode.OVERRIDE:
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(target, fileData.Data);
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            if (File.Exists(targetFile))
                                File.Delete(targetFile);
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(target, fileData.Data);
                            break;
                        case DistributeMode.CREATEONLY:
                            if (File.Exists(targetFile))
                                throw new Exception($"The file {targetFile} does not exist.");
                            File.WriteAllBytes(target, fileData.Data);
                            break;
                        default:
                            throw new Exception($"Unkown distribute mode {settings.Mode}");
                    }
                    logger.Info($"file {targetFile} was copied - Mode {settings.Mode}");
                    fileResults.Add(new FileResult() { Success = true, ReportName = reportName, Message = "File copy successfully.", CopyPath = targetFile });
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

        public Task<HubResult> UploadToHub(HubSettings settings, List<JobResultFileData> fileDataList, Report report, Q2g.HelperQlik.Connection hubConnection)
        {
            var hubResult = new HubResult();
            var reportName = report?.Name ?? null;

            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report filename is empty.");

                var workDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var hubUri = Q2g.HelperQlik.Connection.BuildQrsUri(hubConnection.ConnectUri, hubConnection.Config.ServerUri);
                var hub = new QlikQrsHub(hubUri, hubConnection.ConnectCookie);
                foreach (var fileData in fileDataList)
                {
                    var contentName = $"{Path.GetFileNameWithoutExtension(reportName)} ({Path.GetExtension(fileData.Filename).TrimStart('.').ToUpperInvariant()})";
                    if (hubDeleteAll.Contains(settings.Owner))
                        settings.Mode = DistributeMode.CREATEONLY;

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
                                        //create only mode not over give old report back
                                        hubInfo = sharedContent;
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
                                uploadResult.Message = $"Upload {contentName} successfully.";
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
                    else if (settings.Mode == DistributeMode.DELETEALLFIRST)
                    {
                        var hubUser = new DomainUser(settings.Owner);
                        var hubRequest = new HubSelectRequest()
                        {
                            Filter = HubSelectRequest.GetNameFilter(contentName),
                        };
                        var sharedContentInfos = hub.GetSharedContentAsync(new HubSelectRequest())?.Result;
                        if (sharedContentInfos == null)
                            return null;

                        foreach (var sharedContent in sharedContentInfos)
                        {
                            var serMetaType = sharedContent.MetaData.Where(m => m.Key == "ser-type" && m.Value == "report").SingleOrDefault() ?? null;
                            if (sharedContent.MetaData == null)
                                serMetaType = new MetaData();

                            if (serMetaType != null && sharedContent.Owner.ToString() == hubUser.ToString())
                                hub.DeleteSharedContentAsync(new HubDeleteRequest() { Id = sharedContent.Id.Value }).Wait();
                        }

                        settings.Mode = DistributeMode.CREATEONLY;
                        hubDeleteAll.Add(settings.Owner);
                        return UploadToHub(settings, fileDataList, report, hubConnection);
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
                    foreach (var fileData in fileDataList)
                    {
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
                    foreach (var attach in report.ReportPaths)
                    {
                        mailMessage.Attachments.Add(attach);
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
                    mailResult.Message = "Mail send successfully.";
                    mailResults.Add(mailResult);
                }
                return mailResults;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The reports could not be send as mail.");
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