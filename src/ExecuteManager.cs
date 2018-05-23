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
    #endregion

    public class ExecuteManager
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        public string OnDemandDownloadLink { get; set; }
        private static SerConnection GlobalConnection;
        private List<string> hubDeleteAll;
        private Dictionary<string, string> pathMapper;
        #endregion

        public ExecuteManager()
        {
            hubDeleteAll = new List<string>();
            pathMapper = new Dictionary<string, string>();
            ServicePointManager.ServerCertificateValidationCallback += ValidateRemoteCertificate;
        }

        #region Private Methods
        private List<JToken> GetConnections(Uri serverUri, string appId, Cookie cookie)
        {
            try
            {
                var results = new List<string>();
                var qlikWebSocket = new QlikWebSocket(serverUri, cookie);
                var isOpen = qlikWebSocket.OpenSocket();
                var response = qlikWebSocket.OpenDoc(appId);
                string handle = String.Empty;
                if (response.ToString().Contains("App already open"))
                    response = qlikWebSocket.GetActiveDoc();
                handle = response["result"]["qReturn"]["qHandle"].ToString();
                response = qlikWebSocket.GetConnections(handle);
                return response["result"]["qConnections"].ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "No qlik lib connections found.");
                return null;
            }
        }

        private string NormalizeLibPath(string path, SerConnection settings)
        {
            var workDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var cookie = new Cookie(settings.Credentials.Key, settings.Credentials.Value);
            var result = UriUtils.NormalizeUri(path);
            var libUri = result.Item1;
            var connections = GetConnections(settings.ServerUri, settings.App, cookie);
            if (connections != null)
            {
                var libResult = connections.FirstOrDefault(n => n["qName"].ToString().ToLowerInvariant() == result.Item2) ?? null;
                if(libResult == null)
                {
                    logger.Error($"No data connection with name {result.Item2} found.");
                    return null;
                }
                var libPath = libResult["qConnectionString"].ToString();
                return Path.Combine(libPath, libUri.LocalPath.Replace("/", "\\").Trim().Trim('\\'));
            }

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

            if (!GlobalConnection.SslVerify)
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
                var thumbprints = GlobalConnection.SslValidThumbprints;
                foreach (var item in thumbprints)
                {
                    try
                    {
                        var uri = new Uri(item.Url);
                        var thumbprint = item.Thumbprint.Replace(":", "").Replace(" ", "");
                        if (thumbprint == cert.GetCertHashString() &&
                           uri.Host.ToLowerInvariant() == requestUri.Host.ToLowerInvariant())
                            return true;
                    }
                    catch { }
                }
            }

            return false;
        }
        #endregion

        public void CopyFile(FileSettings settings, List<string> paths, string reportName)
        {
            try
            {
                var currentConnection = settings?.Connections?.FirstOrDefault();
                GlobalConnection = currentConnection ?? null;
                var target = settings.Target?.ToLowerInvariant()?.Trim() ?? null;
                if(target == null)
                {
                    logger.Error($"No target file path for report {reportName} found.");
                    return;
                }

                if (!target.StartsWith("lib://"))
                {
                    logger.Error($"Target value \"{target}\" is not a lib:// folder.");
                    return;
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
                    switch (settings.Mode)
                    {
                        case DistributeMode.OVERRIDE:
                            Directory.CreateDirectory(targetPath);
                            File.Copy(path, targetFile, true);
                            logger.Info($"file {targetFile} was copied");
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
                            logger.Error($"Unkown distribute mode {settings.Mode}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The copying process could not be execute.");
            }
        }

        public Task UploadToHub(HubSettings settings, List<string> paths, string reportName, bool ondemandMode)
        {
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
                    var newPath = path;
                    if (ondemandMode == true)
                    {
                        newPath = Path.Combine(Path.GetDirectoryName(path), reportName);
                        if (!File.Exists(newPath))
                            File.Move(path, newPath);
                    }

                    if (hubDeleteAll.Contains(settings.Owner))
                        settings.Mode = DistributeMode.CREATEONLY;

                    if (settings.Mode == DistributeMode.OVERRIDE || 
                        settings.Mode == DistributeMode.CREATEONLY)
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
                            if (userObject.Count != 1)
                                throw new Exception($"Too many User found. {result}");
                            hubUserId = new Guid(userObject.First()["id"].ToString());
                        }

                        var sharedContent = GetSharedContentFromUser(hub, contentName, hubUser);
                        if (sharedContent == null)
                        {
                            var createRequest = new HubCreateRequest()
                            {
                                Name = contentName,
                                Description = "Created by Sense Excel Reporting",
                                Data = new ContentData()
                                {
                                    ContentType = $"application/{Path.GetExtension(newPath).Trim('.')}",
                                    ExternalPath = Path.GetFileName(newPath),
                                    FileData = File.ReadAllBytes(newPath),
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
                                        ContentType = $"application/{Path.GetExtension(newPath).Trim('.')}",
                                        ExternalPath = Path.GetFileName(newPath),
                                        FileData = File.ReadAllBytes(newPath),
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

                        if (ondemandMode)
                        {
                            hubInfo = GetSharedContentFromUser(hub, contentName, hubUser);
                            OnDemandDownloadLink = hubInfo?.References?.FirstOrDefault()?.ExternalPath ?? null;
                        }

                        if (hubUserId != null)
                        {
                            var newHubInfo = new HubInfo()
                            {
                                Id = hubInfo.Id,
                                Type = "Qlik report",
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

                            return hub.UpdateSharedContentAsync(changeRequest);
                        }
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
                            if (sharedContent.Owner.ToString() == hubUser.ToString())
                                 hub.DeleteSharedContentAsync(new HubDeleteRequest() { Id = sharedContent.Id.Value }).Wait();
                        }

                        settings.Mode = DistributeMode.CREATEONLY;
                        hubDeleteAll.Add(settings.Owner);
                        UploadToHub(settings, paths, reportName, ondemandMode);
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
                return null;
            }
        }

        public void SendMails(List<MailSettings> settingsList)
        {
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
                    var toAddresses = report.Settings.To?.Split(';') ?? new string[0];
                    var ccAddresses = report.Settings.Cc?.Split(';') ?? new string[0];
                    var bccAddresses = report.Settings.Bcc?.Split(';') ?? new string[0];

                    var mailMessage = new MailMessage()
                    {
                        Subject = report.Settings.Subject,
                    };
                    var msgBody = report.Settings.Message;
                    if (msgBody.Contains("</html>"))
                        mailMessage.IsBodyHtml = true;
                    else
                        mailMessage.Body = msgBody.Replace("{n}", "\r\n");

                    mailMessage.From = new MailAddress(report.ServerSettings.From);
                    foreach (var attach in report.ReportPaths)
                        mailMessage.Attachments.Add(attach);

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

                    var client = new SmtpClient(report.ServerSettings.Host, report.ServerSettings.Port)
                    {
                        Credentials = new NetworkCredential(report.ServerSettings.Username, report.ServerSettings.Password),
                    };
                    logger.Debug("send mail package...");
                    client.Send(mailMessage);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The reports could not be sent as mail.");
            }
        }
    }

    public class EMailReport
    {
        #region Varibales & Properties
        public MailSettings Settings { get; private set; }
        public MailServerSettings ServerSettings { get; private set; }
        public List<Attachment> ReportPaths { get; private set; }
        public JToken MailInfo { get; private set; }
        #endregion

        #region Constructor
        public EMailReport(MailSettings settings, MailServerSettings serverSettings, JToken mailInfo)
        {
            Settings = settings;
            ServerSettings = serverSettings;
            MailInfo = mailInfo;
            ReportPaths = new List<Attachment>();
        }
        #endregion

        #region Methods
        public void AddReport(string reportPath, string name)
        {
            var attachment = new Attachment(reportPath)
            {
                 Name = $"{name}{Path.GetExtension(reportPath)}",
            };

            ReportPaths.Add(attachment);
        }
        #endregion
    }
}