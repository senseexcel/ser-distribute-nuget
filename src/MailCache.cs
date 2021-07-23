namespace Ser.Distribute
{
    #region Usings
    using System;
    using Ser.Api;
    using Ser.Distribute.Settings;
    #endregion

    public class MailCache
    {
        #region Properties
        public MailSettings Settings { get; set; }
        public Report Report { get; set; }
        #endregion
    }
}