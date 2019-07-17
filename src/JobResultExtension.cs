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
            if (FileDataList == null)
                FileDataList = new List<JobResultFileData>();
            FileDataList.AddRange(fileDataList);
        }

        public static List<JobResultFileData> GetData(this JobResult @this)
        {
            if (FileDataList == null)
                return new List<JobResultFileData>();
            return FileDataList;
        }

        public static JobResultFileData GetData(this JobResult @this, string filename, Guid taskId)
        {
            if (String.IsNullOrEmpty(filename))
                return null;
            return FileDataList?.FirstOrDefault(f => f.TaskId == taskId && f?.Filename == filename) ?? null;
        }
        #endregion
    }
}
