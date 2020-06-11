namespace Ser.Distribute
{
    #region Usings
    using Ser.Api;
    using System.Threading;
    #endregion

    public class DistibuteOptions
    {
        public DomainUser SessionUser { get; set; }
        public string PrivateKeyPath { get; set; }
        public CancellationToken? CancelToken { get; set; }
    }
}