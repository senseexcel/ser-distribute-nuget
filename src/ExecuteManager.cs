namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Mail;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Net;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Markdig;
    using Ser.Api;
    using Q2g.HelperQrs;
    using Q2g.HelperQlik;
    using System.Text;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json;
    using System.Web;
    using System.Security.Cryptography.X509Certificates;
    using FluentFTP;
    #endregion

    public class ExecuteManager
    {
        #region Logger
        private readonly static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        private readonly Dictionary<string, string> pathMapper;
        #endregion

        #region Constructor
        public ExecuteManager()
        {
            pathMapper = new Dictionary<string, string>();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            if (ServicePointManager.ServerCertificateValidationCallback == null)
                ServicePointManager.ServerCertificateValidationCallback += ValidationCallback.ValidateRemoteCertificate;
        }
        #endregion

        #region Private Methods
        private string NormalizeLibPath(string path, Connection fileConnection)
        {
            logger.Debug($"Resolve data connection from path '{path}'...");
            var pathSegments = path.Split('/');
            var connectionName = pathSegments?.ElementAtOrDefault(2) ?? null;
            var connections = fileConnection?.CurrentApp?.GetConnectionsAsync().Result ?? null;
            if (connections == null)
                throw new Exception("No data connections receive from qlik.");

            var libResult = connections.FirstOrDefault(n => n.qName == connectionName);
            if (libResult == null)
                throw new Exception($"No data connection with name '{connectionName}' found.");

            var libPath = libResult.qConnectionString.ToString();
            var result = Path.Combine(libPath, String.Join('/', pathSegments, 3, pathSegments.Length - 3));
            logger.Debug($"The resolved path is '{result}'.");
            return result;
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

        private string GetContentName(string reportName, ReportData fileData)
        {
            return $"{Path.GetFileNameWithoutExtension(reportName)} ({Path.GetExtension(fileData.Filename).TrimStart('.').ToUpperInvariant()})";
        }

        private bool IsValidMailAddress(string value)
        {
            try
            {
                var mail = new MailAddress(value);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"No valid mail address: '{value}'.");
                return false;
            }
        }

        private string NormalizeReportName(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        private string GetFormatedState(JobResult jobResult)
        {
            return jobResult.Status.ToString().ToUpperInvariant();
        }
        #endregion

        #region Public Methods
        public List<FTPResult> FtpUpload(FTPSettings settings, Report report, JobResult jobResult)
        {
            var fileResults = new List<FTPResult>();
            var reportName = report?.Name ?? null;
            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report filename is empty.");

                var target = settings?.Target?.Trim() ?? null;
                if (target == null)
                {
                    var message = $"No target ftp path for report '{reportName}' found.";
                    logger.Error(message);
                    jobResult.Exception = ReportException.GetException(message);
                    jobResult.Status = TaskStatusInfo.ERROR;
                    fileResults.Add(new FTPResult() { Success = false, ReportState = "ERROR", Message = message, ReportName = reportName });
                    return fileResults;
                }

                var ftpClient = new FtpClient(settings.Host, settings.Port, settings.UserName, settings.Password);
                var ftpEncryptionMode = FtpEncryptionMode.None;
                if (settings?.EncryptionMode != null)
                    ftpEncryptionMode = (FtpEncryptionMode)Enum.Parse(typeof(FtpEncryptionMode), settings.EncryptionMode);
                ftpClient.EncryptionMode = ftpEncryptionMode;
                ftpClient.ValidateAnyCertificate = settings.UseSsl;
                ftpClient.Connect();

                var targetPath = String.Empty;
                if (pathMapper.ContainsKey(target))
                    targetPath = pathMapper[target];
                else
                {
                    targetPath = target;
                    pathMapper.Add(target, targetPath);
                }

                var fileCount = 0;
                foreach (var reportPath in report.Paths)
                {
                    if (report.Paths.Count > 1)
                        fileCount++;
                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var targetFtpFile = $"{targetPath}/{NormalizeReportName(reportName)}{Path.GetExtension(reportPath)}";
                    if (fileCount > 0)
                        targetFtpFile = $"{targetPath}/{NormalizeReportName(reportName)}_{fileCount}{Path.GetExtension(reportPath)}";
                    logger.Debug($"ftp distibute mode {settings.Mode}");

                    var ftpRemoteExists = FtpRemoteExists.Overwrite;
                    switch (settings.Mode)
                    {
                        case DistributeMode.CREATEONLY:
                            ftpRemoteExists = FtpRemoteExists.Skip;
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            logger.Debug($"The FTP file '{targetFtpFile}' could not deleted.");
                            ftpClient.DeleteFile(targetFtpFile);
                            ftpRemoteExists = FtpRemoteExists.Skip;
                            break;
                        case DistributeMode.OVERRIDE:
                            ftpRemoteExists = FtpRemoteExists.Overwrite;
                            break;
                        default:
                            throw new Exception($"Unkown distribute mode {settings.Mode}");
                    }

                    // Create FTP Folder
                    var folderObject = ftpClient.GetObjectInfo(targetPath);
                    if (folderObject == null)
                        ftpClient.CreateDirectory(targetPath, true);

                    // Upload File
                    var ftpStatus = ftpClient.UploadFile(reportPath, targetFtpFile, ftpRemoteExists);
                    if (ftpStatus.IsSuccess())
                        fileResults.Add(new FTPResult() 
                        { 
                            Success = true, 
                            ReportState = GetFormatedState(jobResult), 
                            ReportName = reportName, 
                            Message = "FTP upload was executed successfully.", 
                            FtpPath = targetFtpFile 
                        });
                    else
                        throw new Exception($"The FTP File '{targetFtpFile}' upload failed.");
                }

                return fileResults;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery process for 'ftp' failed.");
                jobResult.Exception = ReportException.GetException(ex);
                jobResult.Status = TaskStatusInfo.ERROR;
                fileResults.Add(new FTPResult() { Success = false, ReportState = "ERROR", Message = ex.Message, ReportName = reportName });
                return fileResults;
            }
        }

        public List<FileResult> CopyFile(FileSettings settings, Report report, Connection fileConnection, JobResult jobResult)
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
                    var message = $"No target file path for report '{reportName}' found.";
                    logger.Error(message);
                    jobResult.Exception = ReportException.GetException(message);
                    jobResult.Status = TaskStatusInfo.ERROR;
                    fileResults.Add(new FileResult() { Success = false, ReportState = "ERROR", Message = message, ReportName = reportName });
                    return fileResults;
                }

                target = target.Replace("\\", "/");
                if (!target.ToLowerInvariant().StartsWith("lib://"))
                {
                    var message = $"Target value '{target}' is not a 'lib://' connection.";
                    logger.Error(message);
                    jobResult.Exception = ReportException.GetException(message);
                    jobResult.Status = TaskStatusInfo.ERROR;
                    fileResults.Add(new FileResult() { Success = false, ReportState = "ERROR", Message = message, ReportName = reportName });
                    return fileResults;
                }

                var targetPath = String.Empty;
                if (pathMapper.ContainsKey(target))
                    targetPath = pathMapper[target];
                else
                {
                    targetPath = NormalizeLibPath(target, fileConnection);
                    pathMapper.Add(target, targetPath);
                }

                logger.Info($"Use the following resolved path '{targetPath}'...");

                var fileCount = 0;
                foreach (var reportPath in report.Paths)
                {
                    var targetFile = Path.Combine(targetPath, $"{NormalizeReportName(reportName)}{Path.GetExtension(reportPath)}");
                    if (report.Paths.Count > 1)
                    {
                        fileCount++;
                        targetFile = Path.Combine(targetPath, $"{NormalizeReportName(reportName)}_{fileCount}{Path.GetExtension(reportPath)}");
                    }

                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    logger.Info($"Copy with distibute mode '{settings.Mode}'...");
                    switch (settings.Mode)
                    {
                        case DistributeMode.OVERRIDE:
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(targetFile, fileData.DownloadData);
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            if (File.Exists(targetFile))
                                File.Delete(targetFile);
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(targetFile, fileData.DownloadData);
                            break;
                        case DistributeMode.CREATEONLY:
                            if (File.Exists(targetFile))
                                throw new Exception($"The file '{targetFile}' does not exist.");
                            File.WriteAllBytes(targetFile, fileData.DownloadData);
                            break;
                        default:
                            throw new Exception($"Unkown distribute mode {settings.Mode}");
                    }
                    logger.Info($"The file '{targetFile}' was copied...");
                    fileResults.Add(new FileResult() 
                    { 
                        Success = true, 
                        ReportState = GetFormatedState(jobResult), 
                        ReportName = reportName, 
                        Message = "Report was successful created.", 
                        CopyPath = targetFile 
                    });
                }
                return fileResults;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery process for 'files' failed.");
                jobResult.Exception = ReportException.GetException(ex);
                jobResult.Status = TaskStatusInfo.ERROR;
                fileResults.Add(new FileResult() { Success = false, ReportState = "ERROR", Message = ex.Message, ReportName = reportName });
                return fileResults;
            }
            finally
            {
                fileConnection.IsFree = true;
            }
        }

        public void DeleteReportsFromHub(HubSettings settings, Report report, Q2g.HelperQlik.Connection hubConnection, DomainUser sessionUser)
        {
            try
            {
                var reportOwner = sessionUser.ToString();
                if (settings.Owner != null)
                    reportOwner = settings.Owner;

                var hubUri = Connection.BuildQrsUri(hubConnection.ConnectUri, hubConnection.Config.ServerUri);
                var hub = new QlikQrsHub(hubUri, hubConnection.ConnectCookie);
                var sharedContentInfos = hub.GetSharedContentAsync(new HubSelectRequest())?.Result;
                if (sharedContentInfos == null)
                    logger.Debug("No shared content found.");

                foreach (var reportPath in report.Paths)
                {
                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var contentName = GetContentName(report?.Name ?? null, fileData);
                    var sharedContentList = sharedContentInfos.Where(s => s.Name == contentName).ToList();
                    foreach (var sharedContent in sharedContentList)
                    {
                        var serMetaType = sharedContent.MetaData.Where(m => m.Key == "ser-type" && m.Value == "report").SingleOrDefault() ?? null;
                        if (sharedContent.MetaData == null)
                            serMetaType = new MetaData();
                        if (serMetaType != null && sharedContent.Owner.ToString().ToLowerInvariant() == reportOwner.ToLowerInvariant())
                            hub.DeleteSharedContentAsync(new HubDeleteRequest() { Id = sharedContent.Id.Value }).Wait();
                    }
                }

                settings.Mode = DistributeMode.CREATEONLY;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Reports could not delete");
            }
        }

        public List<HubResult> UploadToHub(HubSettings settings, Report report, Connection hubConnection, DomainUser sessionUser, JobResult jobResult)
        {
            var reportName = report?.Name ?? null;

            if (String.IsNullOrEmpty(reportName))
                throw new Exception("The report filename is empty.");

            var hubUri = Connection.BuildQrsUri(hubConnection.ConnectUri, hubConnection.Config.ServerUri);
            var hub = new QlikQrsHub(hubUri, hubConnection.ConnectCookie);
            var results = new List<HubResult>();
            foreach (var reportPath in report.Paths)
            {
                try
                {
                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var contentName = GetContentName(reportName, fileData);

                    // Copy with report name - Important for other delivery options.
                    var uploadCopyReportPath = Path.Combine(Path.GetDirectoryName(reportPath), $"{reportName}{Path.GetExtension(reportPath)}");
                    File.Copy(reportPath, uploadCopyReportPath, true);

                    if (settings.Mode == DistributeMode.OVERRIDE ||
                        settings.Mode == DistributeMode.CREATEONLY)
                    {
                        var uploadResult = new HubResult()
                        {
                            ReportName = reportName,
                        };


                        HubInfo hubInfo = null;
                        Guid? hubUserId = null;
                        DomainUser hubUser = sessionUser;
                        if (settings.Owner != null)
                        {
                            logger.Debug($"Use Owner '{settings.Owner}'.");
                            hubUser = new DomainUser(settings.Owner);
                            var filter = $"userId eq '{hubUser.UserId}' and userDirectory eq '{hubUser.UserDirectory}'";
                            var result = hub.SendRequestAsync("user", HttpMethod.Get, null, filter).Result;
                            logger.Debug($"User result: {result}");
                            if (result == null || result == "[]")
                                throw new Exception($"Qlik user {settings.Owner} was not found or session not connected (QRS).");
                            var userObject = JArray.Parse(result);
                            if (userObject.Count > 1)
                                throw new Exception($"Too many User found. {result}");
                            else if (userObject.Count == 1)
                                hubUserId = new Guid(userObject.First()["id"].ToString());
                            logger.Debug($"hubUser id is '{hubUserId}'.");
                        }
                        var sharedContent = GetSharedContentFromUser(hub, contentName, hubUser);
                        if (sharedContent == null)
                        {
                            var createRequest = new HubCreateRequest()
                            {
                                Name = contentName,
                                ReportType = settings.SharedContentType,
                                Description = "Created by Sense Excel Reporting",
                                Tags = new List<Tag>()
                                        {
                                            new Tag()
                                            {
                                                 Name = "SER",
                                                 CreatedDate = DateTime.Now,
                                                 ModifiedDate = DateTime.Now
                                            }
                                        },
                                Data = new ContentData()
                                {
                                    ContentType = $"application/{Path.GetExtension(fileData.Filename).Trim('.')}",
                                    ExternalPath = Path.GetFileName(uploadCopyReportPath),
                                    FileData = fileData.DownloadData,
                                }
                            };

                            logger.Debug($"Create request '{JsonConvert.SerializeObject(createRequest)}'");
                            hubInfo = hub.CreateSharedContentAsync(createRequest).Result;
                            logger.Debug($"Create response '{JsonConvert.SerializeObject(hubInfo)}'");
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
                                        ExternalPath = Path.GetFileName(uploadCopyReportPath),
                                        FileData = fileData.DownloadData,
                                    }
                                };

                                logger.Debug($"Update request '{JsonConvert.SerializeObject(updateRequest)}'");
                                hubInfo = hub.UpdateSharedContentAsync(updateRequest).Result;
                                logger.Debug($"Update response '{JsonConvert.SerializeObject(hubInfo)}'");
                            }
                            else
                            {
                                throw new Exception($"The shared content {contentName} already exist.");
                            }
                        }

                        if (hubUserId != null)
                        {
                            //change shared content owner
                            logger.Debug($"Change shared content owner {hubUserId} (User: '{hubUser}').");
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
                            logger.Debug($"Update Owner request '{JsonConvert.SerializeObject(changeRequest)}'");
                            var ownerResult = hub.UpdateSharedContentAsync(changeRequest).Result;
                            logger.Debug($"Update Owner response '{JsonConvert.SerializeObject(ownerResult)}'");
                        }

                        // Get fresh shared content infos
                        var filename = Path.GetFileName(uploadCopyReportPath);
                        filename = filename.Replace("+", " ");
                        hubInfo = GetSharedContentFromUser(hub, contentName, hubUser);
                        logger.Debug("Get shared content link.");
                        var link = hubInfo?.References?.FirstOrDefault(r => r.LogicalPath.Contains($"/{filename}"))?.ExternalPath ?? null;
                        if (link == null)
                            throw new Exception($"The download link is empty. Please check the security rules. (Name: {filename} - References: {hubInfo?.References?.Count}) - User: {hubUser}.");
                        results.Add(new HubResult() 
                        { 
                            Success = true, 
                            ReportState = GetFormatedState(jobResult), 
                            Message = "Upload to the hub was successful.", 
                            Link = link 
                        });
                    }
                    else
                    {
                        throw new Exception($"Unknown hub mode {settings.Mode}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "The delivery process for 'hub' failed.");
                    jobResult.Exception = ReportException.GetException(ex);
                    jobResult.Status = TaskStatusInfo.ERROR;
                    results.Add(new HubResult() { Success = false, ReportState = "ERROR", Message = ex.Message });
                }
                finally
                {
                    hubConnection.IsFree = true;
                }
            }
            return results;
        }

        public List<MailResult> SendMails(List<MailSettings> settingsList, DistibuteOptions options, JobResult jobResult)
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
                    var reportNames = new StringBuilder();
                    foreach (var report in mailSettings.MailReports)
                    {
                        foreach (var path in report.Paths)
                        {
                            logger.Debug($"Report Name: {report.Name}");
                            reportNames.Append($"{report.Name},");
                            var fileData = report.Data.FirstOrDefault(f => Path.GetFileName(path) == f.Filename);
                            var result = mailList.SingleOrDefault(m => m.Settings.ToString() == mailSettings.ToString());
                            if (result == null)
                            {
                                logger.Debug("Add report to mail");
                                var mailReport = new EMailReport(mailSettings, mailSettings.MailServer, mailSettings.ToString());
                                mailReport.AddReport(fileData, report.Name);
                                mailList.Add(mailReport);
                            }
                            else
                            {
                                logger.Debug("Merge report to mail");
                                result.AddReport(fileData, reportNames.ToString()?.Trim()?.TrimEnd(','));
                            }
                        }
                    }
                }

                //send merged mail infos
                logger.Debug($"{mailList.Count} Mails to send.");
                foreach (var report in mailList)
                {
                    mailResult = new MailResult();
                    mailMessage = new MailMessage
                    {
                        BodyEncoding = Encoding.UTF8,
                        SubjectEncoding = Encoding.UTF8
                    };
                    mailResult.ReportName = report.ReportNames;
                    mailResult.To = report.Settings.To.Replace(";", ",").TrimEnd(',');
                    var toAddresses = report.Settings.To?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    var ccAddresses = report.Settings.Cc?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    var bccAddresses = report.Settings.Bcc?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    mailMessage.Subject = report.Settings?.Subject?.Trim() ?? "NO SUBJECT !!! :(";
                    logger.Debug($"Subject: {mailMessage.Subject}");
                    mailResult.Subject = mailMessage.Subject;
                    var msgBody = report.Settings?.Message?.Trim() ?? String.Empty;
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
                    logger.Debug($"Set mail body '{msgBody}'");
                    mailMessage.Body = msgBody;
                    logger.Debug($"Set from address '{report.ServerSettings.From}'");
                    mailMessage.From = new MailAddress(report.ServerSettings.From);
                    if (report.Settings.SendAttachment)
                    {
                        foreach (var attach in report.ReportPaths)
                        {
                            logger.Debug($"Add attachment '{attach.Name}'.");
                            mailMessage.Attachments.Add(attach);
                        }
                    }

                    foreach (var address in toAddresses)
                        if (IsValidMailAddress(address))
                            mailMessage.To.Add(address);

                    foreach (var address in ccAddresses)
                        if (IsValidMailAddress(address))
                            mailMessage.CC.Add(address);

                    foreach (var address in bccAddresses)
                        if (IsValidMailAddress(address))
                            mailMessage.Bcc.Add(address);

                    client = new SmtpClient(report.ServerSettings.Host, report.ServerSettings.Port);

                    logger.Debug($"Set Credential '{report.ServerSettings.Username}'");
                    if (!String.IsNullOrEmpty(report.ServerSettings.Username) && !String.IsNullOrEmpty(report.ServerSettings.Password))
                        client.Credentials = new NetworkCredential(report.ServerSettings.Username, report.ServerSettings.Password);

                    logger.Debug($"Set SSL '{report.ServerSettings.UseSsl}'");
                    client.EnableSsl = report.ServerSettings.UseSsl;

                    if (report.ServerSettings.UseCertificate)
                    {
                        logger.Info($"Search for email certificates with name 'mailcert.*'...");
                        var certFiles = Directory.GetFiles(Path.GetDirectoryName(options.PrivateKeyPath), "mailcert.*", SearchOption.TopDirectoryOnly);
                        foreach (var certFile in certFiles)
                        {
                            logger.Debug($"Load certificate '{certFile}'.");
                            var x509File = new X509Certificate2(certFile);
                            logger.Debug($"Add certificate '{certFile}'.");
                            client.ClientCertificates.Add(x509File);
                        }
                    }

                    var delay = 0;
                    if (report.ServerSettings.SendDelay > 0)
                        delay = report.ServerSettings.SendDelay * 1000;

                    if (mailMessage.To.Count > 0)
                    {
                        logger.Debug("Send mail package...");
                        Task.Delay(delay).ContinueWith(r =>
                        {
                            client.Send(mailMessage);
                            mailResult.Message = "Sending the mail(s) was successful.";
                            mailResult.Success = true;
                            mailResult.ReportState = GetFormatedState(jobResult);
                        }).Wait();
                    }
                    else
                    {
                        mailResult.Message = "Mail without mail Address could not be sent.";
                        logger.Error(mailResult.Message);
                        mailResult.Success = false;
                        mailResult.ReportState = "ERROR";
                        jobResult.Exception = ReportException.GetException(mailResult.Message);
                        jobResult.Status = TaskStatusInfo.ERROR;
                    }
                    mailMessage.Dispose();
                    client.Dispose();

                    mailResults.Add(mailResult);
                }
                return mailResults;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery process for 'mail' failed.");
                if (mailMessage != null)
                    mailMessage.Dispose();
                if (client != null)
                    client.Dispose();
                jobResult.Exception = ReportException.GetException(ex);
                jobResult.Status = TaskStatusInfo.ERROR;
                mailResult.Success = false;
                mailResult.ReportState = "ERROR";
                mailResult.Message = jobResult.Exception.FullMessage;
                mailResults.Add(mailResult);
                return mailResults;
            }
        }

        public void CleanUp()
        {
            try
            {
                pathMapper.Clear();
            }
            catch (Exception ex)
            {
                throw new Exception("The cleanup in distibute failed.", ex);
            }
        }
        #endregion
    }
}