﻿namespace Ser.Distribute.Actions
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
    using Ser.Distribute.Settings;
    using Q2g.HelperPem;
    using Q2g.HelperQlik;
    #endregion

    public class MailAction : BaseAction
    {
        #region Properties
        private List<MailMessage> SummarizedMails { get; set; } = new List<MailMessage>();
        public List<MailSettings> MailSettings { get; set; } = new List<MailSettings>();
        #endregion

        #region Constructor
        public MailAction(JobResult result) : base(result) { }
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

        private MailMessage GetMailFromSummarizedMailList(MailSettings settings)
        {
            foreach (var mail in SummarizedMails)
            {
                var comparisonText = $"{mail?.Subject}|{mail?.Body.Replace("\r\n", "{n}")}|{mail?.To}/{mail?.CC}/{mail?.Bcc}";
                if (comparisonText == settings.ToString())
                    return mail;
            }
            return null;
        }
        #endregion

        #region Public Methods
        public void SendMails()
        {
            try
            {
                logger.Debug("Summarized mails...");
                foreach (var mailSettings in MailSettings)
                {
                    var summarizedMail = GetMailFromSummarizedMailList(mailSettings);
                    if (summarizedMail == null)
                    {
                        logger.Debug("Create new mail for summarized mail list...");
                        var mailTo = mailSettings?.To?.Replace(";", ",")?.TrimEnd(',') ?? "No mail recipient was specified.";
                        var mailFrom = mailSettings?.MailServer?.From ?? "No sender was specified.";
                        summarizedMail = new MailMessage(mailFrom, mailTo)
                        {
                            BodyEncoding = Encoding.UTF8,
                            SubjectEncoding = Encoding.UTF8,
                            Subject = mailSettings?.Subject?.Trim() ?? "No subject was specified.",
                        };

                        var bccAddresses = mailSettings?.Bcc?.Replace(";", ",")?.TrimEnd(',');
                        if (!String.IsNullOrEmpty(bccAddresses))
                            summarizedMail.Bcc.Add(bccAddresses);

                        var ccAddresses = mailSettings?.Cc?.Replace(";", ",")?.TrimEnd(',');
                        if (!String.IsNullOrEmpty(ccAddresses))
                            summarizedMail.Bcc.Add(ccAddresses);

                        var msgBody = mailSettings?.Message?.Trim() ?? String.Empty;
                        switch (mailSettings?.MailType ?? EMailType.TEXT)
                        {
                            case EMailType.TEXT:
                                msgBody = msgBody.Replace("{n}", Environment.NewLine);
                                break;
                            case EMailType.HTML:
                                summarizedMail.IsBodyHtml = true;
                                break;
                            case EMailType.MARKDOWN:
                                summarizedMail.IsBodyHtml = true;
                                msgBody = Markdown.ToHtml(msgBody);
                                break;
                            default:
                                throw new Exception($"Unknown mail type {mailSettings?.MailType}.");
                        }
                        msgBody = msgBody.Trim();
                        logger.Debug($"Set mail body '{msgBody}'...");
                        summarizedMail.Body = msgBody;
                        SummarizedMails.Add(summarizedMail);

                        foreach (var report in JobResult.Reports)
                        {
                            foreach (var path in report.Paths)
                            {
                                var reportData = report.Data.FirstOrDefault(f => Path.GetFileName(path) == f.Filename);
                                if (reportData == null)
                                    throw new Exception($"No data vor path '{path}' found.");
                                logger.Debug($"Attachment file {path}...");
                                summarizedMail.Attachments.Add(new Attachment(new MemoryStream(reportData.DownloadData), $"{report.Name}{Path.GetExtension(reportData.Filename)}"));
                            }
                        }
                    }
                    else
                    {
                        logger.Debug("Duplicate Mail settings was found in summarized mail list...");
                    }
                }

                var mailServers = MailSettings.Select(s => s.MailServer).GroupBy(s => s.Host).Select(s => s.First()).ToList();
                logger.Debug($"Send {SummarizedMails.Count} Mails over {MailSettings.Count} mail servers...");
                foreach (var mailServer in mailServers)
                {
                    var mailResult = new MailResult();

                    try
                    {
                        foreach (var summarizedMail in SummarizedMails)
                        {
                            using var client = new SmtpClient(mailServer.Host, mailServer.Port);
                            var priateKeyPath = HelperUtilities.GetFullPathFromApp(mailServer.PrivateKey);
                            if (!File.Exists(priateKeyPath))
                                logger.Debug("No private key path found...");

                            if (!String.IsNullOrEmpty(mailServer.Username) && !String.IsNullOrEmpty(mailServer.Password))
                            {
                                logger.Debug($"Set mail server credential for user '{mailServer.Username}'...");
                                var password = mailServer.Password;
                                if (mailServer.UseBase64Password && File.Exists(priateKeyPath))
                                {
                                    logger.Debug($"Use private key path {priateKeyPath}...");
                                    var textCrypter = new TextCrypter(priateKeyPath);
                                    password = textCrypter.DecryptText(password);
                                }
                                client.Credentials = new NetworkCredential(mailServer.Username, password);
                            }
                            else
                            {
                                logger.Debug($"No mail server credential found...");
                            }

                            if (mailServer.UseSsl)
                            {
                                logger.Debug($"Use SSL for mail sending...");
                                client.EnableSsl = true;
                            }

                            if (mailServer.UseCertificate && File.Exists(priateKeyPath))
                            {
                                var certifcateFolder = Path.GetDirectoryName(priateKeyPath);
                                logger.Info($"Search for email certificates with name 'mailcert.*' in folder '{certifcateFolder}'...");
                                var certFiles = Directory.GetFiles(certifcateFolder, "mailcert.*", SearchOption.TopDirectoryOnly);
                                foreach (var certFile in certFiles)
                                {
                                    logger.Debug($"Load certificate '{certFile}'.");
                                    var x509File = new X509Certificate2(certFile);
                                    logger.Debug($"Add certificate '{certFile}'.");
                                    client.ClientCertificates.Add(x509File);
                                }
                            }

                            if (summarizedMail.To.Count > 0)
                            {
                                var delay = 0;
                                if (mailServer.SendDelay > 0)
                                {
                                    delay = mailServer.SendDelay * 1000;
                                    logger.Debug($"Wait {delay} milliseconds for the mail to be sent...");
                                }

                                Task.Delay(delay).ContinueWith(r =>
                                {
                                    logger.Debug("Send mail package...");
                                    client.Send(summarizedMail);

                                    mailResult.Message = "The mail was sent successfully.";
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

                            client.Dispose();
                            Results.Add(mailResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"The delivery via 'Mail' failed with server '{mailServer.Host}'.");
                        JobResult.Exception = ReportException.GetException(ex);
                        JobResult.Status = TaskStatusInfo.ERROR;
                        mailResult.Success = false;
                        mailResult.ReportState = "ERROR";
                        mailResult.TaskName = JobResult.TaskName;
                        mailResult.Message = JobResult?.Exception?.FullMessage ?? null;
                        Results.Add(mailResult);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery via 'Mail' failed.");
                JobResult.Exception = ReportException.GetException(ex);
                JobResult.Status = TaskStatusInfo.ERROR;
                var errorResult = new MailResult()
                {
                    Success = false,
                    ReportState = "ERROR",
                    TaskName = JobResult?.TaskName ?? "Unknown Task",
                    Message = JobResult?.Exception?.FullMessage ?? null
                };
                Results.Add(errorResult);
            }
        }
        #endregion
    }
}