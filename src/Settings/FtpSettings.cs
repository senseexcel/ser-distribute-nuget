namespace Ser.Distribute.Settings
{
    using AgApi;
    using Newtonsoft.Json.Converters;
    using Newtonsoft.Json;
    #region Usings
    using System;
    using System.ComponentModel;
    #endregion

    public class FTPSettings : DistributeSettings
    {
        #region Properties
        [JsonProperty, JsonConverter(typeof(StringEnumConverter))]
        [DefaultValue(EncryptionType.RSA256)]
        public EncryptionType EncryptType { get; set; }
        public bool UseBase64Password { get; set; }
        public string Host { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string EncryptionMode { get; set; }
        public bool UseSsl { get; set; }
        public int Port { get; set; } = 21;
        public string RemotePath { get; set; }
        public DistributeMode Mode { get; set; }
        public string PrivateKey { get; set; }
        #endregion
    }
}