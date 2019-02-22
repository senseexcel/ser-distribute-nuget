namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Ser.Api;
    #endregion

    public static class JobResultExtension
    {
        private static List<JobResultFileData> FileDataList = null;

        #region Public Methods
        public static void SetData(this JobResult @this, List<JobResultFileData> fileDataList)
        {
            FileDataList = fileDataList;
        }

        public static List<JobResultFileData> GetData(this JobResult @this)
        {
            if (FileDataList == null)
                return new List<JobResultFileData>();
            return FileDataList;
        }

        public static JobResultFileData GetData(this JobResult @this, string filename)
        {
            if (String.IsNullOrEmpty(filename))
                return null;
            return FileDataList?.FirstOrDefault(f => f?.Filename == filename) ?? null;
        }
        #endregion
    }

    #region Helpers Class
    public class JobResultFileData
    {
        #region Properties
        public string Filename { get; set; }
        public byte[] Data { get; set; }
        #endregion
    }
    #endregion
}
