namespace Ser.Distribute.Actions
{
    #region Usings
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    #endregion

    public class HttpAction : BaseAction
    {
        public HttpAction(JobResult jobResult) : base(jobResult) { }

        public void PostData(Report report, HttpSettings settings)
        {
            var reportName = report?.Name ?? null;

            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report has no filename.");

                var client = new HttpClient()
                {
                    BaseAddress = new Uri($"{settings.Url.Scheme}://{settings.Url.Host}")
                };

                foreach (var reportPath in report.Paths)
                {
                    var content = new StringContent(settings.Body, Encoding.UTF8, settings.MediaType);

                    var response = client.PostAsync(settings.Url, content).Result;
                    var httpResult = new HttpResult()
                    {
                        Message = response.ToString(),
                        ReportName = reportName,
                        ReportState = GetFormatedState(),
                        TaskName = JobResult.TaskName
                    };

                    if (response.IsSuccessStatusCode)
                        httpResult.Success = true;
                    else
                        httpResult.Success = false;
                    Results.Add(httpResult);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery via 'Http' failed.");
                JobResult.Exception = ReportException.GetException(ex);
                JobResult.Status = TaskStatusInfo.ERROR;
                Results.Add(new HttpResult()
                {
                    Success = false,
                    ReportState = "ERROR",
                    TaskName = JobResult.TaskName,
                    Message = ex.Message,
                    ReportName = reportName
                });
            }
        }
    }
}