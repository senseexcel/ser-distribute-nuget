namespace Ser.Distribute.Messenger
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using Newtonsoft.Json.Linq;
    using Ser.Api;
    using Ser.Distribute.Settings;
    #endregion

    public class Slack : BaseMessenger
    {
        public Slack(MessengerSettings settings, JobResult jobResult) : base(settings, jobResult) { }

        public override MessengerResult SendMessage(List<BaseResult> distibuteResults)
        {
            try
            {
                var messageBuilder = new StringBuilder("Hi,");
                if (distibuteResults.Count > 0)
                {
                    messageBuilder.AppendLine("I wanted to inform you that the reports have now been run.");
                    messageBuilder.AppendLine("The results are as follows.");
                }

                foreach (var result in distibuteResults)
                {
                    var message = GetTextMessageFromResult(result);
                    messageBuilder.AppendLine(message);
                }

                if (messageBuilder.Length <= 8)
                {
                    messageBuilder.AppendLine("I would like to inform you that you have not yet selected a delivery method.");
                    messageBuilder.AppendLine("You can choose between Qlik hub, mail, file system and many more.");
                    messageBuilder.AppendLine("You can find more information under the following link.");
                    messageBuilder.AppendLine("https://docs.analyticsgate.com/how-to/change-or-edit-the-reporting-json-script");
                }

                var responseJson = JObject.FromObject(new
                {
                    text = messageBuilder.ToString().Trim()
                });

                var content = new StringContent(responseJson.ToString(), Encoding.UTF8, "application/json");
                var response = Client.PostAsync(Settings.Url, content).Result;
                if (response.IsSuccessStatusCode)
                {
                    return new MessengerResult()
                    {
                        Message = "Message was successfully transferred to Microsoft Teams.",
                        ReportName = "Slack",
                        ReportState = GetFormatedState(),
                        Success = true,
                        TaskName = JobResult.TaskName
                    };
                }

                throw new Exception(response.ToString());
            }
            catch (Exception ex)
            {
                return new MessengerResult()
                {
                    Message = ex.Message,
                    Success = false,
                    TaskName = JobResult.TaskName,
                    ReportState = "ERROR"
                };
            }
        }
    }
}
