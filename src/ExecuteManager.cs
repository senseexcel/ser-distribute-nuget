namespace Ser.Distribute
{
    #region Usings
    using System.Collections.Generic;
    using System.IO;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using System.Net;
    using Q2g.HelperQrs;
    using System.Net.Mail;
    using System.Net.Http;
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    using Ser.Api;
    using Markdig;
    using Qlik.EngineAPI;
    using enigma;
    using ImpromptuInterface;
    using System.Threading;
    using System.Net.WebSockets;
    #endregion

    public class ExecuteManager
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        private static SerConnection GlobalConnection;
        private List<string> hubDeleteAll;
        private Dictionary<string, string> pathMapper;
        private Session SocketSession;
        #endregion

        #region Constructor
        public ExecuteManager()
        {
            hubDeleteAll = new List<string>();
            pathMapper = new Dictionary<string, string>();
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            if (ServicePointManager.ServerCertificateValidationCallback == null)
                ServicePointManager.ServerCertificateValidationCallback += ValidateRemoteCertificate;
        }
        #endregion

        #region Private Methods
        private IDoc GetSessionAppConnection(Uri uri, Cookie cookie, string appId)
        {
            try
            {
                var url = UriUtils.MakeWebSocketFromHttp(uri);
                var connId = Guid.NewGuid().ToString();
                url = $"{url}/app/engineData/identity/{connId}";
                var config = new EnigmaConfigurations()
                {
                    Url = url,
                    CreateSocket = async (Url) =>
                    {
                        try
                        {
                            var webSocket = new ClientWebSocket();
                            webSocket.Options.RemoteCertificateValidationCallback = ValidationCallback.ValidateRemoteCertificate;
                            webSocket.Options.Cookies = new CookieContainer();
                            webSocket.Options.Cookies.Add(cookie);
                            await webSocket.ConnectAsync(new Uri(Url), CancellationToken.None);
                            return webSocket;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Connectioon to Qlik websocket failed.");
                            return null;
                        }
                    },
                };
                SocketSession = Enigma.Create(config);
                var globalTask = SocketSession.OpenAsync();
                globalTask.Wait();
                IGlobal global = Impromptu.ActLike<IGlobal>(globalTask.Result);
                var doc = global.OpenDocAsync(appId).Result;
                logger.Debug("websocket - success");
                return doc;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "create websocket connection was failed.");
                return null;
            }
        }

        private string NormalizeLibPath(string path, SerConnection settings)
        {
            var workDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var cookie = new Cookie(settings.Credentials.Key, settings.Credentials.Value)
            {
                Secure = true,
                Domain = settings.ServerUri.Host,
                Path = "/",
            };
            var result = UriUtils.NormalizeUri(path);
            var libUri = result.Item1;
            var app = GetSessionAppConnection(settings.ServerUri, cookie, settings.App);
            var connections = app?.GetConnectionsAsync().Result ?? null;
            if (connections != null)
            {
                var libResult = connections.FirstOrDefault(n => n.qName.ToLowerInvariant() == result.Item2) ?? null;
                if (libResult == null)
                {
                    logger.Error($"No data connection with name {result.Item2} found.");
                    SocketSession?.CloseAsync()?.Wait();
                    return null;
                }

                var libPath = libResult.qConnectionString.ToString();
                var resultPath = Path.Combine(libPath, libUri.LocalPath.Replace("/", "\\").Trim().Trim('\\'));
                SocketSession?.CloseAsync()?.Wait();
                return resultPath;
            }
            else
                logger.Error("No data connections found.");
            SocketSession?.CloseAsync()?.Wait();
            return null;
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
                if(sharedContent.Owner.ToString() == hubUser.ToString())
                {
                    return sharedContent;
                }
            }

            return null;
        }

        private static bool ValidateRemoteCertificate(object sender, X509Certificate cert, X509Chain chain,
                                                      SslPolicyErrors error)
        {
            if (error == SslPolicyErrors.None)
                return true;

            if (!GlobalConnection?.SslVerify ?? true)
                return true;

            Uri requestUri = null;
            if (sender is HttpRequestMessage hrm)
                requestUri = hrm.RequestUri;
            if (sender is HttpClient hc)
                requestUri = hc.BaseAddress;
            if (sender is HttpWebRequest hwr)
                requestUri = hwr.Address;

            if (requestUri != null)
            {
                var thumbprints = GlobalConnection.SslValidThumbprints ?? new List<SerThumbprint>();
                foreach (var item in thumbprints)
                {
                    try
                    {
                        var uri = new Uri(item.Url);
                        var thumbprint = item.Thumbprint.Replace(":", "").Replace(" ", "").ToLowerInvariant();
                        if (thumbprint == cert.GetCertHashString().ToLowerInvariant() &&
                           uri.Host.ToLowerInvariant() == requestUri.Host.ToLowerInvariant())
                            return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }
        #endregion

        public List<FileResult> CopyFile(FileSettings settings, List<string> paths, string reportName)
        {
            var fileResults = new List<FileResult>();
            try
            {
                var currentConnection = settings?.Connections?.FirstOrDefault();
                GlobalConnection = currentConnection ?? null;
                var target = settings.Target?.ToLowerInvariant()?.Trim() ?? null;
                if(target == null)
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
                    targetPath = NormalizeLibPath(target, currentConnection);
                    if (targetPath == null)
                        throw new Exception("The could not resolved.");
                    pathMapper.Add(target, targetPath);
                }

                logger.Info($"Resolve target path: \"{targetPath}\".");

                foreach (var path in paths)
                {
                    var targetFile = Path.Combine(targetPath, $"{reportName}");
                    logger.Debug($"copy mode {settings.Mode}");
                    switch (settings.Mode)
                    {
                        case DistributeMode.OVERRIDE:
                            Directory.CreateDirectory(targetPath);
                            File.Copy(path, targetFile, true);
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            if (File.Exists(targetFile))
                                File.Delete(targetFile);
                            Directory.CreateDirectory(targetPath);
                            File.Copy(path, targetFile, false);
                            break;
                        case DistributeMode.CREATEONLY:
                            File.Copy(path, targetFile, false);
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
        }

        public Task<HubResult> UploadToHub(HubSettings settings, List<string> paths, string reportName)
        {
            var hubResult = new HubResult();
            try
            {
                var currentConnection = settings?.Connections?.FirstOrDefault();
                GlobalConnection = currentConnection;
                var workDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var hub = new QlikQrsHub(currentConnection.ServerUri, new Cookie(currentConnection.Credentials.Key,
                                                                                 currentConnection.Credentials.Value));
                foreach (var path in paths)
                {
                    var contentName = $"{Path.GetFileNameWithoutExtension(reportName)} ({Path.GetExtension(path).TrimStart('.').ToUpperInvariant()})";
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
                                            ContentType = $"application/{Path.GetExtension(path).Trim('.')}",
                                            ExternalPath = Path.GetFileName(path),
                                            FileData = File.ReadAllBytes(path),
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
                                                ContentType = $"application/{Path.GetExtension(path).Trim('.')}",
                                                ExternalPath = Path.GetFileName(path),
                                                FileData = File.ReadAllBytes(path),
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
                                var filename = Path.GetFileName(path);
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
                        return UploadToHub(settings, paths, reportName);
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
                    foreach (var path in mailSettings.Paths)
                    {
                        var result = mailList.SingleOrDefault(m => m.Settings.ToString() == mailSettings.ToString());
                        if (result == null)
                        {
                            var mailReport = new EMailReport(mailSettings, mailSettings.MailServer, mailSettings.ToString());
                            mailReport.AddReport(path, mailSettings.ReportName);
                            mailList.Add(mailReport);
                        }
                        else
                        {
                            result.AddReport(path, mailSettings.ReportName);
                        }
                    }
                }

                //send merged mail infos
                foreach (var report in mailList)
                {
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
                    mailResult.Message = "Mail sent successfully.";
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