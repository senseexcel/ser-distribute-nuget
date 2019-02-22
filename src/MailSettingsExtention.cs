namespace Ser.Distribute
{
    using Ser.Api;
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Text;
    #endregion

    public static class MailSettingsExtention
    {
        private static List<JobResultFileData> FileDataList = null;

        public static void SetData(this MailSettings @this, List<JobResultFileData> fileListData)
        {
            FileDataList = fileListData;
        }

        public static List<JobResultFileData> GetData(this MailSettings @this)
        {
            return FileDataList;
        }
    }
}
