namespace Ser.Distribute.Model.Settings
{
    #region Usings
    using System;
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

    public interface IDistributeSettings
    {
        #region Properties
        public bool? Active { get; }
        public SettingsType Type { get; }
        #endregion
    }
}