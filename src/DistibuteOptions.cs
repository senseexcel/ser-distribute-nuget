namespace Ser.Distribute
{
    #region Usings
    using Ser.Api;
    using System.Threading;
    #endregion

    public class DistibuteOptions
    {
        public DomainUser sessionUser { get; set; }
        public string PrivateKeyPath { get; set; }
        public CancellationToken? CancelToken { get; set; }
    }
}