namespace Ser.Distribute.Model.Settings
{ 
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Mail;
    using System.Text.Json.Serialization;
    using Newtonsoft.Json.Linq;
    using Ser.Api;
    #endregion

    #region Enumerations
    public enum EMailType
    {
        TEXT,
        HTML,
        MARKDOWN
    }
    #endregion

    public class MailSettings : IDistributeSettings
    {
        #region Properties
        public SettingsType Type { get; private set; } = SettingsType.MAIL;
        public bool? Active { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
        public bool SendAttachment { get; set; } = true;
        public EMailType MailType { get; set; } = EMailType.TEXT;
        public string To { get; set; }
        public string Cc { get; set; }
        public string Bcc { get; set; }
        public MailServerSettings MailServer { get; set; }
        #endregion

        #region Public Methods
        public override string ToString()
        {
            return $"{Subject}|{Message}|{To}/{Cc}/{Bcc}";
        }
        #endregion
    }

    public class MailServerSettings
    {
        #region Properties
        public string Host { get; set; }
        public string From { get; set; }
        public int Port { get; set; } = 25;
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseSsl { get; set; }
        public bool UseCertificate { get; set; }
        public int SendDelay { get; set; } = 0;
        #endregion
    }

    //???? Wircklich nötig
    public class EMailReport
    {
        #region Varibales & Properties
        public MailSettings Settings { get; private set; }
        public MailServerSettings ServerSettings { get; private set; }
        public List<Attachment> ReportPaths { get; private set; }
        public JToken MailInfo { get; private set; }
        public string ReportNames { get; private set; }
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
        public void AddReport(ReportData fileData, string name)
        {
            var attachment = new Attachment(new MemoryStream(fileData.DownloadData), name)
            {
                Name = $"{name}{Path.GetExtension(fileData.Filename)}",
            };
            ReportPaths.Add(attachment);
            ReportNames = name;
        }
        #endregion
    }
}