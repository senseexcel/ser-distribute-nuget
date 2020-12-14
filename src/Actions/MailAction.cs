namespace Ser.Distribute.Actions
{
    #region Usings
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Mail;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading.Tasks;
    using Markdig;
    #endregion

    public class MailAction : BaseAction
    {
        #region Properties
        public string PrivateKeyPath { get; private set; }
        public List<MailSettings> MailSettingsList { get; private set; } = new List<MailSettings>();
        #endregion

        #region Constructor
        public MailAction(JobResult jobResult, string privateKeyPath) : base(jobResult)
        {
            PrivateKeyPath = privateKeyPath;
        }
        #endregion

        #region Private Methods
        private static bool IsValidMailAddress(string value)
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
        #endregion

        #region Public Methods
        public void AddMailSettings(MailSettings mailSettings, Report report)
        {
            mailSettings.Type = SettingsType.MAIL;
            mailSettings.MailReports.Add(report);
            MailSettingsList.Add(mailSettings);
        }

        public void SendMails()
        {
            SmtpClient client = null;
            var mailMessage = new MailMessage();
            var mailResult = new MailResult();

            try
            {
                var mailList = new List<EMailReport>();
                foreach (var mailSettings in MailSettingsList)
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
                                logger.Debug("Add report to mail...");
                                var mailReport = new EMailReport(mailSettings, mailSettings.MailServer, mailSettings.ToString());
                                mailReport.AddReport(fileData, report.Name);
                                mailList.Add(mailReport);
                            }
                            else
                            {
                                logger.Debug("Merge report to mail...");
                                result.AddReport(fileData, reportNames.ToString()?.Trim()?.TrimEnd(','));
                            }
                        }
                    }
                }

                //send merged mail infos
                logger.Debug($"{mailList.Count} Mails to send...");
                foreach (var report in mailList)
                {
                    mailResult = new MailResult();
                    mailMessage = new MailMessage
                    {
                        BodyEncoding = Encoding.UTF8,
                        SubjectEncoding = Encoding.UTF8
                    };
                    mailResult.ReportName = report.ReportNames;
                    mailResult.To = report.Settings?.To?.Replace(";", ",")?.TrimEnd(',') ?? "No mail recipient was specified.";
                    var toAddresses = report.Settings.To?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    var ccAddresses = report.Settings.Cc?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    var bccAddresses = report.Settings.Bcc?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
                    mailMessage.Subject = report.Settings?.Subject?.Trim() ?? "No subject was specified.";
                    logger.Debug($"Subject: {mailMessage.Subject}");
                    mailResult.Subject = mailMessage.Subject;
                    var msgBody = report.Settings?.Message?.Trim() ?? String.Empty;
                    switch (report.Settings.MailType)
                    {
                        case EMailType.TEXT:
                            msgBody = msgBody.Replace("{n}", Environment.NewLine);
                            break;
                        case EMailType.HTML:
                            mailMessage.IsBodyHtml = true;
                            break;
                        case EMailType.MARKDOWN:
                            mailMessage.IsBodyHtml = true;
                            msgBody = Markdown.ToHtml(msgBody);
                            break;
                        default:
                            throw new Exception($"Unknown mail type {report.Settings.MailType}.");
                    }
                    logger.Debug($"Set mail body '{msgBody}'...");
                    mailMessage.Body = msgBody;
                    logger.Debug($"Set from address '{report?.ServerSettings?.From}'...");
                    mailMessage.From = new MailAddress(report?.ServerSettings?.From ?? "No sender was specified.");
                    if (report.Settings.SendAttachment)
                    {
                        foreach (var attach in report.ReportPaths)
                        {
                            logger.Debug($"Add attachment '{attach.Name}'...");
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

                    logger.Debug($"Set Credential '{report.ServerSettings.Username}'...");
                    if (!String.IsNullOrEmpty(report.ServerSettings.Username) && !String.IsNullOrEmpty(report.ServerSettings.Password))
                        client.Credentials = new NetworkCredential(report.ServerSettings.Username, report.ServerSettings.Password);

                    logger.Debug($"Set SSL '{report.ServerSettings.UseSsl}'...");
                    client.EnableSsl = report.ServerSettings.UseSsl;

                    if (report.ServerSettings.UseCertificate)
                    {
                        logger.Info($"Search for email certificates with name 'mailcert.*'...");
                        var certFiles = Directory.GetFiles(Path.GetDirectoryName(PrivateKeyPath), "mailcert.*", SearchOption.TopDirectoryOnly);
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
                            mailResult.TaskName = JobResult.TaskName;
                            mailResult.ReportState = GetFormatedState();
                        }).Wait();
                    }
                    else
                    {
                        mailResult.Message = "Mail without mail Address could not be sent.";
                        logger.Error(mailResult.Message);
                        mailResult.Success = false;
                        mailResult.TaskName = JobResult.TaskName;
                        mailResult.ReportState = "ERROR";
                        JobResult.Exception = ReportException.GetException(mailResult.Message);
                        JobResult.Status = TaskStatusInfo.ERROR;
                    }
                    mailMessage.Dispose();
                    client.Dispose();
                    Results.Add(mailResult);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery via 'Mail' failed.");
                if (mailMessage != null)
                    mailMessage.Dispose();
                if (client != null)
                    client.Dispose();
                JobResult.Exception = ReportException.GetException(ex);
                JobResult.Status = TaskStatusInfo.ERROR;
                mailResult.Success = false;
                mailResult.ReportState = "ERROR";
                mailResult.TaskName = JobResult.TaskName;
                mailResult.Message = JobResult?.Exception?.FullMessage ?? null;
                Results.Add(mailResult);
            }
        }
        #endregion
    }
}