namespace Ser.Distribute.Actions
{
    #region Usings
    using Ser.Api;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Q2g.HelperQlik;
    using System.IO;
    using System.Net;
    using Ser.Distribute.Settings;
    #endregion

    public class FileSystemAction : BaseAction
    {
        #region Properties
        public Dictionary<string, string> PathCache { get; private set; } = new Dictionary<string, string>();
        #endregion

        #region Constructor
        public FileSystemAction(JobResult jobResult) : base(jobResult) { }
        #endregion

        #region Private Methods
        private static string ResolveLibPath(string path, Connection socketConnection)
        {
            logger.Debug($"Resolve path '{path}'...");
            if (path.StartsWith("lib://"))
            {
                logger.Debug($"Resolve data connection from path '{path}'...");

                if (socketConnection == null)
                    throw new Exception("Could not create a connection to Qlik. (FILE)");

                path = path.Replace("\\", "/");
                var pathSegments = path.Split('/');
                var connectionName = pathSegments?.ElementAtOrDefault(2) ?? null;
                var connections = socketConnection?.CurrentApp?.GetConnectionsAsync().Result ?? null;
                if (connections == null)
                    throw new Exception("No data connections receive from qlik.");

                var libResult = connections.FirstOrDefault(n => n.qName == connectionName);
                if (libResult == null)
                    throw new Exception($"No data connection with name '{connectionName}' found.");

                var libPath = libResult.qConnectionString.ToString();
                var result = Path.Combine(libPath, String.Join('/', pathSegments, 3, pathSegments.Length - 3));
                logger.Debug($"The resolved path is '{result}'.");
                return result;
            }
            else
            {
                logger.Debug($"No lib path '{path}'...");
                return path;
            }
        }
        #endregion

        #region Public Methods
        public void CopyFile(Report report, FileSettings settings, Connection socketConnection)
        {
            var reportName = report?.Name ?? null;
            try
            {
                if (String.IsNullOrEmpty(reportName))
                    throw new Exception("The report filename is empty.");

                var target = settings?.Target?.Trim() ?? null;
                if (target == null)
                {
                    var message = $"No target file path for report '{reportName}' found.";
                    logger.Error(message);
                    JobResult.Exception = ReportException.GetException(message);
                    JobResult.Status = TaskStatusInfo.ERROR;
                    Results.Add(new FileResult()
                    {
                        Success = false,
                        ReportState = "ERROR",
                        TaskName = JobResult.TaskName,
                        Message = message,
                        ReportName = reportName
                    });
                    return;
                }

                if (!target.ToLowerInvariant().StartsWith("lib://") && socketConnection != null)
                {
                    var message = $"Target value '{target}' is not a 'lib://' connection.";
                    logger.Error(message);
                    JobResult.Exception = ReportException.GetException(message);
                    JobResult.Status = TaskStatusInfo.ERROR;
                    Results.Add(new FileResult()
                    {
                        Success = false,
                        ReportState = "ERROR",
                        Message = message,
                        TaskName = JobResult.TaskName,
                        ReportName = reportName
                    });
                    return;
                }

                var targetPath = String.Empty;
                if (PathCache.ContainsKey(target))
                    targetPath = PathCache[target];
                else
                {
                    targetPath = ResolveLibPath(target, socketConnection);
                    PathCache.Add(target, targetPath);
                }

                logger.Info($"Use the following resolved path '{targetPath}'...");

                var fileCount = 0;
                foreach (var reportPath in report.Paths)
                {
                    var targetFile = Path.Combine(targetPath, $"{NormalizeReportName(reportName)}{Path.GetExtension(reportPath)}");
                    if (report.Paths.Count > 1)
                    {
                        fileCount++;
                        targetFile = Path.Combine(targetPath, $"{NormalizeReportName(reportName)}_{fileCount}{Path.GetExtension(reportPath)}");
                    }

                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    logger.Info($"Copy with distibute mode '{settings.Mode}'...");
                    switch (settings.Mode)
                    {
                        case DistributeMode.OVERRIDE:
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(targetFile, fileData.DownloadData);
                            break;
                        case DistributeMode.DELETEALLFIRST:
                            if (File.Exists(targetFile))
                                File.Delete(targetFile);
                            Directory.CreateDirectory(targetPath);
                            File.WriteAllBytes(targetFile, fileData.DownloadData);
                            break;
                        case DistributeMode.CREATEONLY:
                            if (File.Exists(targetFile))
                                throw new Exception($"The file '{targetFile}' does not exist.");
                            File.WriteAllBytes(targetFile, fileData.DownloadData);
                            break;
                        default:
                            throw new Exception($"Unkown distribute mode {settings.Mode}");
                    }
                    logger.Info($"The file '{targetFile}' was copied...");
                    Results.Add(new FileResult()
                    {
                        Success = true,
                        ReportState = GetFormatedState(),
                        TaskName = JobResult.TaskName,
                        ReportName = reportName,
                        Message = "Report was successful created.",
                        CopyPath = targetFile
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "The delivery process for 'files' failed.");
                JobResult.Exception = ReportException.GetException(ex);
                JobResult.Status = TaskStatusInfo.ERROR;
                Results.Add(new FileResult()
                {
                    Success = false,
                    ReportState = "ERROR",
                    TaskName = JobResult.TaskName,
                    Message = ex.Message,
                    ReportName = reportName
                });
            }
            finally
            {
                PathCache.Clear();
                if (socketConnection != null)
                    socketConnection.IsFree = true;
            }
        }
        #endregion
    }
}