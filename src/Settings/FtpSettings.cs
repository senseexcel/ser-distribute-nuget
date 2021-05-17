namespace Ser.Distribute.Settings
{
    #region Usings
    using System;
    #endregion

    public class FTPSettings : DistributeSettings
    {
        #region Properties
        public string Host { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string EncryptionMode { get; set; }
        public bool UseSsl { get; set; }
        public int Port { get; set; } = 21;
        public string RemotePath { get; set; }
        public DistributeMode Mode { get; set; }
        #endregion
    }
}