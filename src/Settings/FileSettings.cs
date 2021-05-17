namespace Ser.Distribute.Settings
{
    #region Usings
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Ser.Api;
    using Ser.Api.Model;
    #endregion

    public class FileSettings : DistributeSettings
    {
        #region Properties
        public string Target { get; set; }
        public DistributeMode Mode { get; set; }

        [JsonProperty(nameof(Connections)), JsonConverter(typeof(SingleValueArrayConverter))]
        public List<SerConnection> Connections { get; set; }
        #endregion
    }
}