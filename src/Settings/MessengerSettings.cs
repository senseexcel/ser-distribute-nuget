namespace Ser.Distribute.Settings
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

    public class MessengerSettings : DistributeSettings
    {
        #region Properties
        public MessengerType Messenger { get; set; }
        public Uri Url { get; set; }
        #endregion
    }
}