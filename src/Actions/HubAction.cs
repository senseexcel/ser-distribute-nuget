namespace Ser.Distribute.Actions
{
    #region Usings
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Q2g.HelperQlik;
    using Q2g.HelperQrs;
    using Ser.Api;
    #endregion

    public class HubAction : BaseAction
    {
        #region Constructor
        public HubAction(JobResult jobResult) : base(jobResult) { }
        #endregion

        #region Private Methods
        private static string GetContentName(string reportName, ReportData fileData)
        {
            return $"{Path.GetFileNameWithoutExtension(reportName)} ({Path.GetExtension(fileData.Filename).TrimStart('.').ToUpperInvariant()})";
        }

        private static HubInfo GetSharedContentFromUser(string name, DomainUser hubUser, QlikQrsHub qrsApi)
        {
            var hubRequest = new HubSelectRequest()
            {
                Filter = HubSelectRequest.GetNameFilter(name),
            };

            var sharedContentInfos = qrsApi.GetSharedContentAsync(hubRequest)?.Result;
            if (sharedContentInfos == null)
                return null;

            if (hubUser == null)
                return sharedContentInfos.FirstOrDefault() ?? null;

            foreach (var sharedContent in sharedContentInfos)
            {
                if (sharedContent.Owner.ToString() == hubUser.ToString())
                {
                    return sharedContent;
                }
            }

            return null;
        }

        private static void DeleteReportsFromHub(Report report, HubSettings settings)
        {
            try
            {
                var reportOwner = settings?.SessionUser?.ToString() ?? null;
                if (settings.Owner != null)
                    reportOwner = settings.Owner;

                var qrsApi = settings.GetQrsApiConnection();
                var sharedContentInfos = qrsApi.GetSharedContentAsync(new HubSelectRequest())?.Result;
                if (sharedContentInfos == null)
                    logger.Debug("No shared content found.");

                foreach (var reportPath in report.Paths)
                {
                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var contentName = GetContentName(report?.Name ?? null, fileData);
                    var sharedContentList = sharedContentInfos.Where(s => s.Name == contentName).ToList();
                    foreach (var sharedContent in sharedContentList)
                    {
                        var serMetaType = sharedContent.MetaData.Where(m => m.Key == "ser-type" && m.Value == "report").SingleOrDefault() ?? null;
                        if (sharedContent.MetaData == null)
                            serMetaType = new MetaData();
                        if (serMetaType != null && sharedContent.Owner.ToString().ToLowerInvariant() == reportOwner.ToLowerInvariant())
                            qrsApi.DeleteSharedContentAsync(new HubDeleteRequest() { Id = sharedContent.Id.Value }).Wait();
                    }
                }

                settings.Mode = DistributeMode.CREATEONLY;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Reports could not delete");
            }
        }

        private static string GetFullLink(Uri baseUrl, string conentUrl)
        {
            return $"{baseUrl.Scheme}://{baseUrl.Host}{conentUrl}";
        }
        #endregion

        public void UploadToHub(Report report, HubSettings settings)
        {
            var reportName = report?.Name ?? null;

            if (String.IsNullOrEmpty(reportName))
                throw new Exception("The report filename is empty.");

            if (settings?.SessionUser == null)
                throw new Exception("The session user is empty.");

            //Delete reports from hub before uploaded!
            if (settings.Mode == DistributeMode.DELETEALLFIRST)
                DeleteReportsFromHub(report, settings);

            foreach (var reportPath in report.Paths)
            {
                try
                {
                    var fileData = report.Data.FirstOrDefault(f => f.Filename == Path.GetFileName(reportPath));
                    var contentName = GetContentName(reportName, fileData);

                    // Copy with report name - Important for other delivery options.
                    var uploadCopyReportPath = Path.Combine(Path.GetDirectoryName(reportPath), $"{reportName}{Path.GetExtension(reportPath)}");
                    File.Copy(reportPath, uploadCopyReportPath, true);

                    if (settings.Mode == DistributeMode.OVERRIDE ||
                        settings.Mode == DistributeMode.CREATEONLY)
                    {
                        var uploadResult = new HubResult()
                        {
                            ReportName = reportName,
                        };

                        HubInfo hubInfo = null;
                        Guid? hubUserId = null;
                        DomainUser hubUser = settings?.SessionUser ?? null;
                        var qrsApi = settings.GetQrsApiConnection();
                        if (settings.Owner != null)
                        {
                            logger.Debug($"Use Owner '{settings.Owner}'.");
                            hubUser = new DomainUser(settings.Owner);
                            var filter = $"userId eq '{hubUser.UserId}' and userDirectory eq '{hubUser.UserDirectory}'";
                            var result = qrsApi.SendRequestAsync("user", HttpMethod.Get, null, filter).Result;
                            logger.Debug($"User result: {result}");
                            if (result == null || result == "[]")
                                throw new Exception($"Qlik user {settings.Owner} was not found or session not connected (QRS).");
                            var userObject = JArray.Parse(result);
                            if (userObject.Count > 1)
                                throw new Exception($"Too many User found. {result}");
                            else if (userObject.Count == 1)
                                hubUserId = new Guid(userObject.First()["id"].ToString());
                            logger.Debug($"hubUser id is '{hubUserId}'.");
                        }
                        var sharedContent = GetSharedContentFromUser(contentName, hubUser, qrsApi);
                        if (sharedContent == null)
                        {
                            var createRequest = new HubCreateRequest()
                            {
                                Name = contentName,
                                ReportType = settings.SharedContentType,
                                Description = "Created by Analytics Gate",
                                Tags = new List<Tag>()
                                        {
                                            new Tag()
                                            {
                                                 Name = "SER",
                                                 CreatedDate = DateTime.Now,
                                                 ModifiedDate = DateTime.Now
                                            }
                                        },
                                Data = new ContentData()
                                {
                                    ContentType = $"application/{Path.GetExtension(fileData.Filename).Trim('.')}",
                                    ExternalPath = Path.GetFileName(uploadCopyReportPath),
                                    FileData = fileData.DownloadData,
                                }
                            };

                            logger.Debug($"Create request '{JsonConvert.SerializeObject(createRequest)}'");
                            hubInfo = qrsApi.CreateSharedContentAsync(createRequest).Result;
                            logger.Debug($"Create response '{JsonConvert.SerializeObject(hubInfo)}'");
                        }
                        else
                        {
                            if (settings.Mode == DistributeMode.OVERRIDE)
                            {
                                var tag = sharedContent?.Tags?.FirstOrDefault(t => t.Name == "SER") ?? null;
                                if (tag != null)
                                {
                                    tag.CreatedDate = DateTime.Now;
                                    tag.ModifiedDate = DateTime.Now;
                                }
                                var updateRequest = new HubUpdateRequest()
                                {
                                    Info = sharedContent,
                                    Data = new ContentData()
                                    {
                                        ContentType = $"application/{Path.GetExtension(fileData.Filename).Trim('.')}",
                                        ExternalPath = Path.GetFileName(uploadCopyReportPath),
                                        FileData = fileData.DownloadData,
                                    }
                                };

                                logger.Debug($"Update request '{JsonConvert.SerializeObject(updateRequest)}'");
                                hubInfo = qrsApi.UpdateSharedContentAsync(updateRequest).Result;
                                logger.Debug($"Update response '{JsonConvert.SerializeObject(hubInfo)}'");
                            }
                            else
                            {
                                throw new Exception($"The shared content '{contentName}' already exist.");
                            }
                        }

                        if (hubUserId != null)
                        {
                            //change shared content owner
                            logger.Debug($"Change shared content owner '{hubUserId}' (User: '{hubUser}').");
                            var newHubInfo = new HubInfo()
                            {
                                Id = hubInfo.Id,
                                Type = settings.SharedContentType,
                                Owner = new Owner()
                                {
                                    Id = hubUserId.ToString(),
                                    UserId = hubUser.UserId,
                                    UserDirectory = hubUser.UserDirectory,
                                    Name = hubUser.UserId,
                                }
                            };

                            var changeRequest = new HubUpdateRequest()
                            {
                                Info = newHubInfo,
                            };
                            logger.Debug($"Update Owner request '{JsonConvert.SerializeObject(changeRequest)}'");
                            var ownerResult = qrsApi.UpdateSharedContentAsync(changeRequest).Result;
                            logger.Debug($"Update Owner response '{JsonConvert.SerializeObject(ownerResult)}'");
                        }

                        // Get fresh shared content infos
                        var filename = Path.GetFileName(uploadCopyReportPath);
                        filename = filename.Replace("+", " ");
                        hubInfo = GetSharedContentFromUser(contentName, hubUser, qrsApi);
                        logger.Debug("Get shared content link.");
                        var link = hubInfo?.References?.FirstOrDefault(r => r.LogicalPath.Contains($"/{filename}"))?.ExternalPath ?? null;
                        if (link == null)
                            throw new Exception($"The download link is empty. Please check the security rules. (Name: {filename} - References: {hubInfo?.References?.Count}) - User: {hubUser}.");

                        Results.Add(new HubResult()
                        {
                            Success = true,
                            ReportState = GetFormatedState(),
                            TaskName = JobResult.TaskName,
                            Message = "Upload to the hub was successful.",
                            Link = link,
                            ReportName = contentName,
                            FullLink = GetFullLink(qrsApi.ConnectUri, link)
                        });
                    }
                    else
                    {
                        throw new Exception($"Unknown hub mode {settings.Mode}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "The delivery via 'Hub' failed.");
                    JobResult.Exception = ReportException.GetException(ex);
                    JobResult.Status = TaskStatusInfo.ERROR;
                    Results.Add(new HubResult()
                    {
                        Success = false,
                        ReportState = "ERROR",
                        TaskName = JobResult.TaskName,
                        Message = ex.Message
                    });
                }
                finally
                {
                    settings.SocketConnection.IsFree = true;
                }
            }
        }
    }
}