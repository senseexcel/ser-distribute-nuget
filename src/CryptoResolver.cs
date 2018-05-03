namespace Ser.Distribute
{  
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using Q2g.HelperPem;
    #endregion

    public class CryptoResolver
    {
        #region Logger
        private static Logger logger = LogManager.GetCurrentClassLogger();
        #endregion

        #region Variables & Properties
        private JObject Json { get; set; }
        private TextCrypter Crypter { get; set; }
        #endregion

        #region Constructor
        public CryptoResolver(string privateKeyPath = null)
        {
            if (File.Exists(privateKeyPath))
                Crypter = new TextCrypter(privateKeyPath);
        }
        #endregion

        #region Methods
        private string GetDecyptText(string value)
        {
            try
            {
                if (Crypter != null)
                    value = Crypter.DecryptText(value);
                return value;
            }
            catch
            {
                return value;
            }
        }

        private void ResolveJTokenInternal(JToken jtoken)
        {
            if (jtoken.HasValues)
            {
                ResolveInternal(jtoken.Children());
            }
            else
            {
                var value = jtoken?.Value<string>() ?? String.Empty;
                var parent = jtoken.Parent.Value<JProperty>();
                parent.Value = GetDecyptText(value);
            }
        }

        private void ResolveInternal(JEnumerable<JToken> jtokens)
        {
            foreach (var jtoken in jtokens.Children())
            {
                var array = jtoken as JArray;
                if (array != null)
                {
                    foreach (var item in array)
                    {
                        ResolveJTokenInternal(item);
                    }
                }
                else
                {
                    ResolveJTokenInternal(jtoken);
                }
            }
        }

        public JObject Resolve(JObject value)
        {
            try
            {
                if (Crypter == null)
                    return value;

                if (value == null)
                    return null;

                Json = new JObject(value);
                ResolveInternal(Json.Children());
                return Json;
            }
            catch (Exception ex)
            {
                throw new Exception("The resolve of the evaluate section has an error.", ex);
            }
        }
        #endregion
    }
}