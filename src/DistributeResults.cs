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
        #region Properties
        [JsonProperty(Required = Required.Always)]
        public abstract string DistributionMode { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string TaskName { get; set; }

        [JsonProperty(Required = Required.Always)]
        public bool Success { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string Message { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string ReportName { get; set; }

        [JsonProperty(Required = Required.Always)]
        public string ReportState { get; set; }
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class DistibutionResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "Distibution";
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class ErrorResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "Error";
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                 NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class HubResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "Hub";
        public string Link { get; set; }
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class FileResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "File System";
        public string CopyPath { get; set; }
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class FTPResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "FTP";
        public string FtpPath { get; set; }
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class HttpResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "Http";
        #endregion
    }

    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore,
                NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    public class MailResult : BaseResult
    {
        #region Properties
        public override string DistributionMode { get; set; } = "Mail";
        public string To { get; set; }
        public string Subject { get; set; }
        #endregion
    }
}