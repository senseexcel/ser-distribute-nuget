namespace Ser.Distribute
{
    #region Usings
    using NLog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    #endregion

    public class UriUtils
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        public static string MakeWebSocketFromHttp(Uri uri)
        {
            try
            {
                var result = uri.AbsoluteUri;
                result = result.Replace("http://", "ws://");
                result = result.Replace("https://", "wss://");
                result = result.TrimEnd('/');
                return result;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Make web socket from http was failed.");
                return null;
            }
        }

        public static Tuple<Uri, string> NormalizeUri(string input)
        {
            try
            {
                var uri = new Uri(input);
                return new Tuple<Uri, string>(uri, uri.Host);
            }
            catch
            {
                logger.Debug("Read uri in compatibility mode");
                var tempUri = input.Replace("://", "://host/");
                var uri = new Uri(tempUri);
                var parts = input.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var host = uri.OriginalString.Split('/').ElementAtOrDefault(3);
                var segment = String.Join('/', parts.Skip(2)).TrimEnd('/');
                var normalUri = new Uri($"{uri.Scheme}://host/{segment}");
                return new Tuple<Uri, string>(normalUri, host);
            }
        }
    }
}