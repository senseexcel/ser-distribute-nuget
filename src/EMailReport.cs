namespace Ser.Distribute
{
    #region Usings
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.Net.Mail;
    using System.Text;
    using Newtonsoft.Json.Linq;
    using System.IO;
    #endregion

    public class EMailReport
    {
        #region Varibales & Properties
        public MailSettings Settings { get; private set; }
        public MailServerSettings ServerSettings { get; private set; }
        public List<Attachment> ReportPaths { get; private set; }
        public JToken MailInfo { get; private set; }
        #endregion

        #region Constructor
        public EMailReport(MailSettings settings, MailServerSettings serverSettings, JToken mailInfo)
        {
            Settings = settings;
            ServerSettings = serverSettings;
            MailInfo = mailInfo;
            ReportPaths = new List<Attachment>();
        }
        #endregion

        #region Methods
        public void AddReport(JobResultFileData fileData, string name)
        {
            var attachment = new Attachment(new MemoryStream(fileData.Data), name)
            {
                Name = $"{name}{Path.GetExtension(fileData.Filename)}",
            };

            ReportPaths.Add(attachment);
        }
        #endregion
    }
}
