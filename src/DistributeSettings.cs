namespace Ser.Distribute
{
    #region Usings
    using System.Collections.Generic;
    using Newtonsoft.Json.Serialization;
    using Newtonsoft.Json;
    using Ser.Api;
    using System.Net;
    using Q2g.HelperQlik;
    using Q2g.HelperQrs;
    using System;
    #endregion

    #region Enumerations
    public enum EMailType
    {
        TEXT,
        HTML,
        MARKDOWN
    }

    public enum SettingsType
    {
        MAIL,
        FILE,
        HUB,
        FTP,
        MESSENGER
    }

    public enum MessengerType
    {
        MICROSOFTTEAMS,
        SLACK
    }

    public enum DistributeMode
    {
        CREATEONLY,
        OVERRIDE,
        DELETEALLFIRST
    }
    #endregion

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class DistibuteSettings
    {
        #region Properties
        public bool? Active { get; set; }
        public SettingsType Type { get; set; }
        #endregion
    }

    public class FileSettings : DistibuteSettings
    {
        #region Properties
        public string Target { get; set; }
        public DistributeMode Mode { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }

        [JsonIgnore]
        public Connection SocketConnection { get; set; }
        #endregion
    }

    public class HubSettings : DistibuteSettings
    {
        #region Properties && Variables
        private QlikQrsHub qrsApi;
        public string SharedContentType { get; set; } = "Qlik report";
        public string Owner { get; set; }
        public DistributeMode Mode { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }

        [JsonIgnore]
        public Connection SocketConnection { get; set; }

        [JsonIgnore]
        public DomainUser SessionUser { get; set; }
        #endregion

        public QlikQrsHub GetQrsApiConnection()
        {
            try
            {
                if (qrsApi != null)
                    return qrsApi;
                
                var hubUri = Connection.BuildQrsUri(SocketConnection?.ConnectUri ?? null, SocketConnection?.Config?.ServerUri ?? null);
                qrsApi = new QlikQrsHub(hubUri, SocketConnection.ConnectCookie);
                return qrsApi;
            }
            catch (Exception ex)
            {
                throw new Exception("No connection to the QRS API could be established.", ex);
            }
        }
    }

    public class FTPSettings : DistibuteSettings
    {
        #region Properties
        public string Host { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string EncryptionMode { get; set; }
        public bool UseSsl { get; set; }
        public int Port { get; set; } = 21;
        public string RemotePath { get; set; }
        public DistributeMode Mode { get; set; }
        #endregion
    }

    public class MessengerSettings : DistibuteSettings
    {
        #region Properties
        public MessengerType Messenger { get; set; }
        public Uri Url { get; set; }

        [JsonIgnore]
        public JobResult JobResult { get; set; }
        #endregion
    }

    public class MailSettings : DistibuteSettings
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

        [JsonIgnore]
        public List<Report> MailReports { get; set; } = new List<Report>();
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
}