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
                var uri1 = input.Replace("://", "://host/");
                var uri = new Uri(uri1);
                var fragments = uri.Fragment.Split('/');
                var host = uri.OriginalString.Split('/').ElementAtOrDefault(3);
                var uri2 = String.Join('/', fragments.Skip(1)).TrimEnd('/');
                var normalUri = new Uri($"{uri.Scheme}://host/{uri2}");
                return new Tuple<Uri, string>(normalUri, host);
            }
        }
    }
}