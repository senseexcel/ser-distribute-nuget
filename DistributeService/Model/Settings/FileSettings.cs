namespace Ser.Distribute.Model.Settings
{
    #region Usings
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Ser.Api;
    using Ser.Api.Model;
    #endregion

    public class FileSettings : IDistributeSettings
    {
        #region Properties
        public SettingsType Type { get; private set; } = SettingsType.FILE;
        public bool? Active { get; set; }
        public string Target { get; set; }
        public DistributeMode Mode { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }
        #endregion
    }
}