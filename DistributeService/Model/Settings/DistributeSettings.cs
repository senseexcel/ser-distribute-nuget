namespace Ser.Distribute.Model.Settings
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Ser.Distribute.Model.Settings;
    #endregion

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class DistributeSettings
    {
        [JsonProperty]
        public MailSettings Mail { get; set; }

        [JsonProperty]
        public FileSettings File { get; set; }

        [JsonProperty]
        public HubSettings Hub { get; set; }

        [JsonProperty]
        public FTPSettings Ftp { get; set; }

        [JsonProperty]
        public MessengerSettings Messenger { get; set; }
    }
}