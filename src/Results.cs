namespace Ser.Distribute
{
    #region Usings
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    #endregion

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public abstract class BaseResult
    {
        [JsonProperty(Required = Required.Always)]
        public abstract string DistributionMode { get; set; }

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
        public override string DistributionMode { get; set; } = "Hub";

        public string Link { get; set; }
        
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class FileResult : BaseResult
    {
        public override string DistributionMode { get; set; } = "File System";

        public string CopyPath { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class FTPResult : BaseResult
    {
        public override string DistributionMode { get; set; } = "FTP";

        public string FtpPath { get; set; }
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MailResult : BaseResult
    {
        public override string DistributionMode { get; set; } = "Mail";

        public string To { get; set; }
        public string Subject { get; set; }
    }
}