namespace Ser.Distribute.Model.Messenger
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using Ser.Api;
    #endregion

    public abstract class BaseMessenger
    {
        #region Properties
        public HttpClient Client { get; private set; }
        public MessengerSettings Settings { get; private set; }
        #endregion

        #region Constructor
        public BaseMessenger(MessengerSettings settings)
        {
            Settings = settings;
            Client = new HttpClient()
            {
                BaseAddress = new Uri($"{Settings?.Url?.Scheme}://{Settings?.Url?.Host}")
            };
        }
        #endregion

        #region Private Methods
        private static T CastResult<T>(BaseResult value) where T : BaseResult, new()
        {
            if (typeof(T).Equals(typeof(T)))
                return (T)Convert.ChangeType(value, typeof(T));
            return null;
        }
        #endregion

        #region Protected Methods
        protected string GetFormatedState()
        {
            return  Settings?.JobResult?.Status.ToString()?.ToUpperInvariant() ?? "Unknown state";
        }

        protected static string GetTextMessageFromResult(BaseResult distibuteResult)
        {
            if (distibuteResult.Success)
            {
                if (distibuteResult.GetType() == typeof(HubResult))
                {
                    var hubResult = CastResult<HubResult>(distibuteResult);
                    return $"Click on the following link {hubResult.FullLink} to open the report on the hub.";
                }
                else if (distibuteResult.GetType() == typeof(FTPResult))
                {
                    var ftpResult = CastResult<FTPResult>(distibuteResult);
                    return $"Click on the following link {ftpResult.FtpPath} to open the report on the ftp server.";
                }
                else if (distibuteResult.GetType() == typeof(FileResult))
                {
                    var fileResult = CastResult<FileResult>(distibuteResult);
                    return $"The file was saved to the path '{fileResult.CopyPath}'.";
                }
                else if (distibuteResult.GetType() == typeof(MailResult))
                {
                    var mailResult = CastResult<MailResult>(distibuteResult);
                    return $"The Mail was sent to '{mailResult.To}'.";
                }
                else
                {
                    throw new Exception($"Unknown result type '{distibuteResult?.GetType()?.Name ?? null}'.");
                }
            }
            else
            {
                return $"The generation of the report '{distibuteResult.ReportName}' has an error.";
            }
        }

        protected static string GetHtmlMessageFromResult(BaseResult distibuteResult)
        {
            if (distibuteResult.Success)
            {
                if (distibuteResult.GetType() == typeof(HubResult))
                {
                    var hubResult = CastResult<HubResult>(distibuteResult);
                    return $"<p>Click on the following link <a href=\"{hubResult.FullLink}\">{hubResult.ReportName}</a> to open the report on the hub.</p>";
                }
                else if(distibuteResult.GetType() == typeof(FTPResult))
                {
                    var ftpResult = CastResult<FTPResult>(distibuteResult);
                    return $"<p>Click on the following link <a href=\"{ftpResult.FtpPath}\">{ftpResult.ReportName}</a> to open the report on the ftp server.</p>";
                }
                else if (distibuteResult.GetType() == typeof(FileResult))
                {
                    var fileResult = CastResult<FileResult>(distibuteResult);
                    return $"<p>The file was saved to the path '{fileResult.CopyPath}'.</p>";
                }
                else if (distibuteResult.GetType() == typeof(MailResult))
                {
                    var mailResult = CastResult<MailResult>(distibuteResult);
                    return $"<p>The Mail was sent to '{mailResult.To}'.</p>";
                }
                else
                {
                    throw new Exception($"Unknown result type '{distibuteResult?.GetType()?.Name ?? null}'.");
                }
            }
            else
            {
                return $"The generation of the report '{distibuteResult.ReportName}' has an error.";
            }
        }
        #endregion

        #region Public Methods
        public abstract MessengerResult SendMessage(List<BaseResult> distibuteResults);
        #endregion
    }
}