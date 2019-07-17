namespace Ser.Distribute
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Text;
    #endregion

    public class JobResultFileData
    {
        #region Properties
        public Guid TaskId { get; set; }
        public string Filename { get; set; }
        public byte[] Data { get; set; }
        #endregion
    }
}
