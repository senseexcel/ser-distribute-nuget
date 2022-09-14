namespace Ser.Distribute.Actions
{
    #region Usings
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using FluentFTP;
    using NLog;
    using Ser.Distribute.Settings;
    using Q2g.HelperQlik;
    using AgApi;
    using Q2g.HelperPem;
    #endregion

    public class FtpAction : BaseAction
    {
        #region Constructor
        public FtpAction(JobResult jobResult) : base(jobResult) { }
        #endregion

        #region Public Methods
        public void FtpUpload(Report report, FTPSettings settings)
        {
            var reportName = report?.Name ?? null;

            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report has no filename.");

                var targetPath = settings?.RemotePath?.Trim() ?? null;
                if (targetPath == null)
                {
                    var message = $"No target ftp path for report '{reportName}' found.";
                    logger.Error(message);
                    JobResult.Exception = ReportException.GetException(message);
                    JobResult.Status = TaskStatusInfo.ERROR;
                    Results.Add(new FTPResult()
                    {
                        Success = false,
                        ReportState = "ERROR",
                        TaskName = JobResult.TaskName,
                        Message = message,
                        ReportName = reportName
                    });
                    return;
                }

                var priateKeyPath = HelperUtilities.GetFullPathFromApp(settings.PrivateKey);
                if (!File.Exists(priateKeyPath))
                    logger.Debug("No private key path found...");

                logger.Debug($"Set ftp server credential for user '{settings.Username}'...");
                var password = settings.Password;
                if (settings.UseBase64Password)
                {
                    logger.Debug($"Use private key path '{priateKeyPath}' for encryption '{settings.EncryptType}' ...");
                    if (settings.EncryptType == EncryptionType.RSA256)
                    {
                        var textCrypter = new TextCrypter(priateKeyPath);
                        password = textCrypter.DecryptText(password);
                    }
                    else if (settings.EncryptType == EncryptionType.AES256)
                    {
                        var aesCrypter = new AesCrypter(priateKeyPath);
                        password = aesCrypter.DecryptText(password);
                    }
                    else
                    {
                        throw new Exception($"Unknown encryption type '{settings.EncryptType}'.");
                    }
                }

                var ftpClient = new FtpClient(settings.Host, settings.Port, settings.Username, password);
                var ftpEncryptionMode = FtpEncryptionMode.None;
                if (settings?.EncryptionMode != null)
                    ftpEncryptionMode = (FtpEncryptionMode)Enum.Parse(typeof(FtpEncryptionMode), settings.EncryptionMode);
                ftpClient.EncryptionMode = ftpEncryptionMode;
                ftpClient.ValidateAnyCertificate = settings.UseSsl;
                ftpClient.Connect();

                var fileCount = 0;
                foreach (var reportPath in report.Paths)
                {
                    if (report.Paths.Count > 1)
                        fileCount++;
                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var targetFtpFile = $"{targetPath}/{NormalizeReportName(reportName)}{Path.GetExtension(reportPath)}";
                    if (fileCount > 0)
                        targetFtpFile = $"{targetPath}/{NormalizeReportName(reportName)}_{fileCount}{Path.GetExtension(reportPath)}";
                    logger.Debug($"ftp distibute mode {settings.Mode}");

                    var ftpRemoteExists = FtpRemoteExists.Overwrite;
                    switch (settings.Mode)
                    {
                        case DistributeMode.CREATEONLY:
                            ftpRemoteExists = FtpRemoteExists.Skip;
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            logger.Debug($"The FTP file '{targetFtpFile}' could not deleted.");
                            if (ftpClient.FileExists(targetFtpFile))
                                ftpClient.DeleteFile(targetFtpFile);
                            ftpRemoteExists = FtpRemoteExists.Skip;
                            break;
                        case DistributeMode.OVERRIDE:
                            ftpRemoteExists = FtpRemoteExists.Overwrite;
                            break;
                        default:
                            throw new Exception($"Unkown distribute mode {settings.Mode}");
                    }

                    // Create FTP Folder
                    var folderObject = ftpClient.GetObjectInfo(targetPath);
                    if (folderObject == null)
                        ftpClient.CreateDirectory(targetPath, true);

                    // Upload File
                    var ftpStatus = ftpClient.UploadFile(reportPath, targetFtpFile, ftpRemoteExists);
                    if (ftpStatus == FtpStatus.Success)
                        Results.Add(new FTPResult()
                        {
                            Success = true,
                            ReportState = GetFormatedState(),
                            ReportName = reportName,
                            TaskName = JobResult.TaskName,
                            Message = "FTP upload was executed successfully.",
                            FtpPath = $"ftp://{settings.Host}{targetFtpFile}"
                        });
                    else
                        throw new Exception($"The FTP File '{targetFtpFile}' upload failed.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery via 'ftp' failed.");
                JobResult.Exception = ReportException.GetException(ex);
                JobResult.Status = TaskStatusInfo.ERROR;
                Results.Add(new FTPResult()
                {
                    Success = false,
                    ReportState = "ERROR",
                    TaskName = JobResult.TaskName,
                    Message = ex.Message,
                    ReportName = reportName
                });
            }
        }
        #endregion
    }
}