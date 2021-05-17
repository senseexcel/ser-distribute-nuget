namespace Ser.Distribute.Settings
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Ser.Distribute.Settings;
    #endregion

    #region Enumerations
    public enum SettingsType
    {
        MAIL,
        FILE,
        HUB,
        FTP,
        MESSENGER
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
    public class DistributeSettings
    {
        #region Properties
        public bool Active { get; set; }
        public SettingsType Type { get; set; }
        #endregion
    }
}