namespace Ser.Distribute.Model.Messenger
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using Newtonsoft.Json.Linq;
    using Ser.Api;
    #endregion

    public class MicrosoftTeams : BaseMessenger
    {
        #region Constructor
        public MicrosoftTeams(MessengerSettings settings) : base(settings) { }
        #endregion

        #region Public Methods
        public override MessengerResult SendMessage(List<BaseResult> distibuteResults)
        {
            try
            {
                var messageBuilder = new StringBuilder("Hi,<br/>");
                if(distibuteResults.Count > 0)
                {
                    messageBuilder.AppendLine("<p>I wanted to inform you that the reports have now been run.</p>");
                    messageBuilder.AppendLine("<p>The results are as follows.</p>");
                }

                foreach (var result in distibuteResults)
                {
                    var message = GetHtmlMessageFromResult(result);
                    messageBuilder.AppendLine(message);
                }

                if (messageBuilder.Length <= 8)
                {
                    messageBuilder.AppendLine("<p>I would like to inform you that you have not yet selected a delivery method.</p>");
                    messageBuilder.AppendLine("<p>You can choose between Qlik hub, mail, file system and many more.</p>");
                    messageBuilder.AppendLine("<p>You can find more information under the following link.</p>");
                    messageBuilder.AppendLine("<p><a href=\"https://docs.analyticsgate.com/how-to/change-or-edit-the-reporting-json-script\">Overview</a></p>");
                }

                var responseJson = JObject.FromObject(new
                {
                    contentType = "html",
                    title = "AG Reporting Results",
                    text = messageBuilder.ToString().Trim()
                });

                var content = new StringContent(responseJson.ToString(), Encoding.UTF8, "application/json");
                var response = Client.PostAsync(Settings.Url, content).Result;
                if (response.IsSuccessStatusCode)
                {
                    return new MessengerResult()
                    {
                        Message = "Message was successfully transferred to Microsoft Teams.",
                        ReportName = "Microsoft Teams",
                        ReportState = GetFormatedState(),
                        Success = true,
                        TaskName = Settings.JobResult.TaskName
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
                    TaskName = Settings.JobResult.TaskName,
                    ReportState = "ERROR"
                };
            }
        }
        #endregion
    }
}