namespace Ser.Distribute
{
    #region Usings
    using System.Collections.Generic;
    using Newtonsoft.Json.Serialization;
    using Newtonsoft.Json;
    using Ser.Api;
    using System.Net;
    #endregion

    #region Enumerations
    /// <summary>
    /// Type of the email body
    /// </summary>
    public enum EMailType
    {
        /// <summary>
        /// Use mail plain text in the mail body
        /// </summary>
        TEXT,

        /// <summary>
        /// Use html code in the mail body
        /// </summary>
        HTML,

        /// <summary>
        /// Use markdown syntax in the mail body
        /// </summary>
        MARKDOWN
    }

    /// <summary>
    /// Distribute type
    /// </summary>
    public enum SettingsType
    {
        /// <summary>
        /// Distribute type mail
        /// </summary>
        MAIL,

        /// <summary>
        ///  Distribute type file
        /// </summary>
        FILE,

        /// <summary>
        ///  Distribute type hub
        /// </summary>
        HUB,

        /// <summary>
        /// Distibute type ftp and ftps
        /// </summary>
        FTP
    }

    /// <summary>
    /// Mode how the delivery should behave.
    /// </summary>
    public enum DistributeMode
    {
        /// <summary>
        /// Create only new content or trigger an exception.
        /// </summary>
        CREATEONLY,

        /// <summary>
        /// Override the content or file on distribute.
        /// </summary>
        OVERRIDE,

        /// <summary>
        /// Delete all content before distribute
        /// </summary>
        DELETEALLFIRST
    }
    #endregion

    #region Interfaces
    /// <summary>
    /// Basic interfaces to identify the delivery type
    /// </summary>
    public interface ISettings
    {
        /// <summary>
        /// The type of the settings
        /// </summary>
        SettingsType Type { get; set; }

        /// <summary>
        /// Activate distribute
        /// </summary>
        bool? Active { get; set; }
    }
    #endregion

    /// <summary>
    /// Base settings
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class BaseDeliverySettings : ISettings
    {
        #region Variables & Properties
        /// <summary>
        /// Activate distribute
        /// </summary>
        public bool? Active { get; set; }

        /// <summary>
        /// Distribute type
        /// </summary>
        public SettingsType Type { get; set; }
        #endregion
    }

    /// <summary>
    /// Base setting for distribute
    /// </summary>
    public class DeliverySettings : BaseDeliverySettings
    {
        #region Properties
        /// <summary>
        /// target location for distribute
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// distribute mode
        /// </summary>
        public DistributeMode Mode { get; set; }

        /// <summary>
        /// All connections that can be used.
        /// </summary>
        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }
        #endregion
    }

    /// <summary>
    /// The settings for file distribute
    /// </summary>
    public class FileSettings : DeliverySettings {}

    /// <summary>
    /// The general settings for web connections
    /// </summary>
    public class WebDeliverySettings : DeliverySettings
    {
        /// <summary>
        /// Server name
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// User name
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// User password in plain text or signed
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// The port on the server
        /// </summary>
        public int Port { get; set; } = 21;
    }

    /// <summary>
    /// The settings for FTP or FTP-SSL distibute
    /// </summary>
    public class FTPSettings : WebDeliverySettings
    {
        /// <summary>
        /// Implicit oder Explicit
        /// </summary>
        public string EncryptionMode { get; set; }

        /// <summary>
        /// Use a SSL Certificate
        /// </summary>
        public bool UseSsl { get; set; }
    }

    /// <summary>
    /// The settings for hub distribute
    /// </summary>
    public class HubSettings : DeliverySettings
    {
        /// <summary>
        /// The content type of the report.
        /// </summary>
        public string SharedContentType { get; set; } = "Qlik report";

        /// <summary>
        /// The user to be the owner.
        /// </summary>
        public string Owner { get; set; }
    }

    /// <summary>
    /// The settings for mail distribute
    /// </summary>
    public class MailSettings : BaseDeliverySettings
    {
        #region Variables & Properties
        /// <summary>
        /// The subject of the mail
        /// </summary>
        public string Subject { get; set; }

        /// <summary>
        /// The message of the mail
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Send email with a Attachment or no.
        /// </summary>
        public bool SendAttachment { get; set; } = true;

        /// <summary>
        /// The body type of the mail
        /// </summary>
        public EMailType MailType { get; set; } = EMailType.TEXT;

        /// <summary>
        /// The recipients of the email
        /// </summary>
        public string To { get; set; }

        /// <summary>
        /// The copy recipients of the email
        /// </summary>
        public string Cc { get; set; }

        /// <summary>
        /// The blind copy recipients of the email
        /// </summary>
        public string Bcc { get; set; }

        /// <summary>
        /// The mail server settings
        /// </summary>
        public MailServerSettings MailServer { get; set; }

        /// <summary>
        /// Cache of mail reports befor make a package
        /// </summary>
        [JsonIgnore]
        public List<Report> MailReports { get; set; } = new List<Report>();
        #endregion

        /// <summary>
        /// gets the full string of the mail
        /// </summary>
        /// <returns>Full string of the mail</returns>
        public override string ToString()
        {
            return $"{Subject}|{Message}|{To}/{Cc}/{Bcc}";
        }
    }

    /// <summary>
    /// The settings of the mail server.
    /// </summary>
    public class MailServerSettings
    {
        #region Variables & Properties
        /// <summary>
        /// The host machine
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// The sender of the mail
        /// </summary>
        public string From { get; set; }

        /// <summary>
        /// The port of the mail server
        /// </summary>
        public int Port { get; set; } = 25;

        /// <summary>
        /// The username of the mail account (optional).
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// The password of the mail account (optional).
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Use ssl of authentication (optional).
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>
        /// Use ssl Certificate in Reporting Certificate Folder with Name 'mailcert' (optional).
        /// </summary>
        public bool UseCertificate { get; set; }

        /// <summary>
        /// Use this property to send mail with a delay (optional).
        /// Value in seconds.
        /// </summary>
        public int SendDelay { get; set; } = 0;
        #endregion
    }
}