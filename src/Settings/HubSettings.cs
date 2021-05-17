namespace Ser.Distribute.Settings
{ 
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;
    using Ser.Api;
    using Ser.Api.Model;
    #endregion

    public class HubSettings : DistributeSettings
    {
        #region Properties && Variables
        public string SharedContentType { get; set; } = "Qlik report";
        public string Owner { get; set; }
        public DistributeMode Mode { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }
        #endregion
    }
}