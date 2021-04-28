namespace Ser.Distribute.Model.Settings
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    #endregion

    #region Enumerations
    public enum MessengerType
    {
        MICROSOFTTEAMS,
        SLACK
    }
    #endregion

    public class MessengerSettings : IDistributeSettings
    {
        #region Properties
        public SettingsType Type { get; private set; } = SettingsType.MESSENGER;
        public bool? Active { get; set; }
        public MessengerType Messenger { get; set; }
        public Uri Url { get; set; }
        #endregion
    }
}