namespace Ser.Distribute
{
    #region Usings
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using NLog;
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    #endregion

    #region Enumerations
    public enum EMailType
    {
        TEXT,
        HTML,
        MARKUP
    }

    public enum SettingsType
    {
        MAIL,
        FILE,
        HUB
    }

    public enum DistributeMode
    {
        OVERRIDE,
        DELETEALLFIRST,
        CREATEONLY
    }
    #endregion

    public interface ISettings
    {
        SettingsType Type { get; set; }
        bool? Active { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class BaseDeliverySettings : ISettings
    {
        #region Variables & Properties
        public bool? Active { get; set; }
        public SettingsType Type { get; set; }  
        #endregion
    }

    public class DeliverySettings: BaseDeliverySettings
    {
        #region Properties
        public string Target { get; set; }
        public DistributeMode Mode { get; set; }
        public string Owner { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }
        #endregion
    }

    public class FileSettings : DeliverySettings
    {
        // properties for the future   
        #region Variables & Properties
        public string Group;
        public string ACL; 
        #endregion
    }

    public class HubSettings : DeliverySettings { }

    public class MailSettings : BaseDeliverySettings
    {
        #region Variables & Properties       
        public string Subject { get; set; }
        public string Message { get; set; }
        public EMailType MailType { get; set; } = EMailType.TEXT;
        public string To { get; set; }
        public string Cc { get; set; }
        public string Bcc { get; set; }
        public MailServerSettings MailServer { get; set; }      
        [JsonIgnore]
        public List<string> Paths { get; set; }
        [JsonIgnore]
        public string ReportName { get; set; }
        #endregion

        public override string ToString()
        {
            return $"{Subject}|{Message}|{To}/{Cc}/{Bcc}";
        }
    }

    public class MailServerSettings
    {
        #region Variables & Properties
        public string Host { get; set; }
        public string From { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseSsl { get; set; }
        #endregion
    }
}