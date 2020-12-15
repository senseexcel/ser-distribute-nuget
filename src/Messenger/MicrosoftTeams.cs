namespace Ser.Distribute.Messenger
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
                var messageBuilder = new StringBuilder();
                foreach (var result in distibuteResults)
                {
                    var message = GetHtmlMessageFromResult(result);
                    messageBuilder.AppendLine(message);
                }

                var responseJson = JObject.FromObject(new
                {
                    contentType = "html",
                    title = "Message from AG Reporting",
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
                    ReportState = "ERRROR"
                };
            }
        }
        #endregion
    }
}