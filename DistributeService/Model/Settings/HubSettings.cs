namespace Ser.Distribute.Model.Settings
{ 
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Ser.Api;
    using Ser.Api.Model;
    #endregion

    public class HubSettings : IDistributeSettings
    {
        #region Properties && Variables
        public SettingsType Type { get; private set; } = SettingsType.HUB;
        public bool? Active { get; set; }
        public string SharedContentType { get; set; } = "Qlik report";
        public string Owner { get; set; }
        public DistributeMode Mode { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }
        #endregion
    }
}