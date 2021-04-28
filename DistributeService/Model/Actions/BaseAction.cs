namespace Ser.Distribute.Model.Actions
{    
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NLog;
    using Ser.Api;
    #endregion

    public class BaseAction
    {
        #region Logger
        protected readonly static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Properties
        public JobResult JobResult { get; private set; }
        public List<BaseResult> Results { get; private set; } = new List<BaseResult>();
        #endregion

        #region Constructor
        public BaseAction(JobResult jobResult)
        {
            JobResult = jobResult ?? throw new Exception("The job result must not be null.");
        }
        #endregion

        #region Protected Methods
        protected static string NormalizeReportName(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        protected string GetFormatedState()
        {
            return JobResult?.Status.ToString()?.ToUpperInvariant() ?? "Unknown state";
        }
        #endregion
    }
}