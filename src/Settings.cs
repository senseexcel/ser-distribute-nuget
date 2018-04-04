namespace SerDistribute
{
    #region Usings
    using Newtonsoft.Json;
    using NLog;
    using SerApi;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    #endregion

    #region Enumerations
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
    }

    public class BaseDeliverySettings : ISettings
    {
        #region Variables & Properties
        public bool? Active { get; set; }
        public SerConnection Connection { get; set; }
        public string Target { get; set; }
        public DistributeMode Mode { get; set; }
        public SettingsType Type { get; set; }
        public string Owner { get; set; }
        #endregion
    }

    public class FileSettings : BaseDeliverySettings
    {
        #region Variables & Properties
        // future      
        public string Group;
        public string ACL; 
        #endregion
    }

    public class HubSettings : BaseDeliverySettings { }

    public class MailSettings : ISettings
    {
        #region Variables & Properties
        public bool? Active { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
        public MailAddresses EMail { get; set; }
        public MailServerSettings MailServer { get; set; }
        public SettingsType Type { get; set; }
        [JsonIgnore]
        public List<string> Paths { get; set; }
        [JsonIgnore]
        public string ReportName { get; set; }
        #endregion
    }

    public class MailAddresses
    {
        #region Variables & Properties
        public string To { get; set; }
        public string Cc { get; set; }
        public string Bcc { get; set; }
        #endregion
    }

    public class MailServerSettings
    {
        #region Variables & Properties
        public string Host { get; set; }
        public string From { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        #endregion
    }
}
