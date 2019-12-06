namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Newtonsoft.Json.Serialization;
    #endregion

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class BaseResult
    {
        [JsonProperty(Required = Required.Always)]
        public bool Success { get; set; }
        [JsonProperty(Required = Required.Always)]
        public string Message { get; set; }
        [JsonProperty(Required = Required.Always)]
        public string ReportName { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                 NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class HubResult : BaseResult
    {
        public string Link { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class FileResult : BaseResult
    {
        public string CopyPath { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MailResult : BaseResult 
    { 
        public string To { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class DistributeResults
    {
        public List<HubResult> HubResults { get; set; } = new List<HubResult>();
        public List<FileResult> FileResults { get; set; } = new List<FileResult>();
        public List<MailResult> MailResults { get; set; } = new List<MailResult>();
    }
}
