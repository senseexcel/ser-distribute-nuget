namespace Ser.Distribute.Settings
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

    public class MailSettings : DistributeSettings
    {
        #region Properties
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
            return $"{Subject.Trim()}|{Message.Trim()}|{To}/{Cc}/{Bcc}";
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
        public bool UseBase64Password { get; set; }
        public int SendDelay { get; set; } = 0;
        public string PrivateKey { get; set; }
        #endregion
    }
}