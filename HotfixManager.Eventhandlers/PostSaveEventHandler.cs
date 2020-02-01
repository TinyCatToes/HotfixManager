using System;
using System.Net;
using System.IO;
using System.Xml.Linq;
using System.Data.SqlClient;
using System.Collections;
using System.Runtime.InteropServices;
using kCura.EventHandler;
using kCura.EventHandler.CustomAttributes;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;
using Relativity.Services.FileField;
using System.Threading.Tasks;
using Relativity.Kepler.Transport;
using Relativity.Services.Interfaces.InstanceSetting;
using Relativity.Services.Interfaces.InstanceSetting.Model;
using Relativity.Services.Objects.DataContracts;
using Relativity.Services.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Data;

namespace HotfixManager.EventHandlers
{
    [kCura.EventHandler.CustomAttributes.Description("Post Save Event Handler that parses a hotfix package and auto-updates fields on the page.")]
    [System.Runtime.InteropServices.Guid("853FEDAE-3A07-4A5C-8830-AAE62BF855E1")]
    [kCura.EventHandler.CustomAttributes.RunTarget(kCura.EventHandler.Helper.RunTargets.Workspace)]
    public class HotfixParseHandler : kCura.EventHandler.PostSaveEventHandler
    {
        string PackageDestinationFolderPath = "PREINIT";
        string PackageDestinationFilePath = "PREINIT";
        string PackageDestinationUnzippedPath = "PREINIT";
        private const string ENQUEUE_QUERY = @"insert into EDDS.eddsdbo.HotfixParseQueue values (@PackageArtifactID,@WorkspaceArtifactID,1,'',100,GETUTCDATE(),@ActingUser)";
        private IAPILog logger;

        public override Response Execute()
        {
            // Update Security Protocol
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            logger = Helper.GetLoggerFactory().GetLogger().ForContext<HotfixParseHandler>();

            //Construct a response object with default values.
            kCura.EventHandler.Response retVal = new kCura.EventHandler.Response();
            retVal.Success = true;
            retVal.Message = string.Empty;
            
            //construct reference objects for download
            var FileField = new FieldRef
            {
                Guid = Constants.Constants.UPLOAD_FILE_FIELD
            };
            var CurrentObject = new RelativityObjectRef
            {
                ArtifactID = this.ActiveArtifact.ArtifactID
            };

            //attempt to save package and unzip it
            try
            {
                logger.LogDebug("Hotfix: Attempting to download file from field {field} on object {object}", FileField.ArtifactID, CurrentObject.ArtifactID);
                downloadAndSaveFileToDisk(FileField, CurrentObject);
            }
            catch (Exception ex)
            {
                updateInlineFieldsWithResult(false, ex.ToString());
                retVal.Success = false;
                retVal.Message = ex.ToString();
            }


            //attempt to open master manifest and parse package-level XML
            try
            {
                parsePackgeLevelXml();
            }
            catch (Exception ex)
            {
                updateInlineFieldsWithResult(false, ex.ToString());
                retVal.Success = false;
                retVal.Message = ex.ToString();
            }

            if (retVal.Success == true)
            {//work is complete, add row to ParseQueue and exit with success.           
                try
                {
                    SqlParameter actingUser = new SqlParameter("ActingUser", SqlDbType.Int);
                    SqlParameter selfArtifactID = new SqlParameter("PackageArtifactID", SqlDbType.Int);
                    SqlParameter workspaceArtifactID = new SqlParameter("WorkspaceArtifactID", SqlDbType.Int);
                    actingUser.Value = Helper.GetAuthenticationManager().UserInfo.ArtifactID;
                    selfArtifactID.Value = this.ActiveArtifact.ArtifactID;
                    workspaceArtifactID.Value = Helper.GetActiveCaseID();
                    Helper.GetDBContext(Helper.GetActiveCaseID()).ExecuteNonQuerySQLStatement(ENQUEUE_QUERY, new List<SqlParameter> { selfArtifactID, workspaceArtifactID, actingUser });
                    updateInlineFieldsWithResult(true);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Error attempting to enqueue parse job.");
                    updateInlineFieldsWithResult(false, ex.ToString());
                    retVal.Success = false;
                    retVal.Message = ex.ToString();
                }

            }
            return retVal;
        } //end Execute


        //this writes parsing results back to the inline field on the RDO for convenience.
        //handles its own exceptions internally and does not rethrow them (flow can continue if this method fails)
        private void updateInlineFieldsWithResult(bool result, string Message = "")
        {
            string StatusMessage;
            switch (result)
            {
                case true:
                    StatusMessage = "Queued";
                    break;
                case false:
                    StatusMessage = "Error";
                    break;
                default:
                    StatusMessage = "Undefined";
                    break;
            }
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var CurrentObject = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID };
                var parseStatusFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.PARSE_STATUS_FIELD },
                    Value = StatusMessage
                };
                var parseErrorFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.PARSE_ERROR_FIELD },
                    Value = Message
                };

                var updateRequest = new UpdateRequest
                {
                    Object = CurrentObject,
                    FieldValues = new List<FieldRefValuePair> { parseStatusFVP, parseErrorFVP }
                };
                try
                {
                    var objManResult = objectManager.UpdateAsync(Helper.GetActiveCaseID(), updateRequest).Result;
                }
                catch (AggregateException ex)
                {//print error for each exception in aggregate, then end.
                    foreach (var exchild in ex.InnerExceptions)
                    {
                        logger.LogError(exchild, "Hotfix: Error When Updating Inline Results Fields");
                    }
                    return;
                }                
            }

        } //end updateInlineFieldsWithResult

        //this downloads the zip from the file field and pastes it into the EDDSFileshare. Targets a folder named for the current object's artifact ID.
        //also unzips package to a subfolder.
        private void downloadAndSaveFileToDisk(FieldRef FileField, RelativityObjectRef Target)
        {
            //grab EDDSfileshare from instance settings
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var queryRequest = new QueryRequest();
                queryRequest.ObjectType = new ObjectTypeRef() { ArtifactTypeID = 42 };
                queryRequest.Fields = new List<FieldRef>()
                {
                    new FieldRef() { Name = "Value" }
                };
                queryRequest.Condition = "(('Section' == 'Relativity.Data' AND 'Name' == 'EDDSFileShare'))";                                            
                try
                {
                    Relativity.Services.Objects.DataContracts.QueryResult qresult = objectManager.QueryAsync(-1, queryRequest, 1, 1).Result;
                    string resultValue = qresult.Objects[0].FieldValues[0].Value.ToString();

                    // check if trailing slash exists
                    if (!resultValue.Substring(resultValue.Length - 1, 1).Equals(@"\"))
                    {
                        PackageDestinationFolderPath = resultValue + @"\Hotfix\" + Helper.GetActiveCaseID() + "_" + this.ActiveArtifact.ArtifactID.ToString();
                    }
                    else
                    {
                        PackageDestinationFolderPath = resultValue + @"Hotfix\" + Helper.GetActiveCaseID() + "_" + this.ActiveArtifact.ArtifactID.ToString();
                    }
                    PackageDestinationFilePath = PackageDestinationFolderPath + @"\Package.zip";
                    PackageDestinationUnzippedPath = PackageDestinationFolderPath + @"\Expanded";
                    logger.LogDebug("Hotfix: Successfully retrieved EDDSFileshare from Instance Settings: {path}", resultValue);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Error When Getting EDDSFileshare");
                    throw;
                }
            }

            //read file field into filestream and save out to EDDSFileshare. then, unzip package to subdir
            using (IFileFieldManager proxy = Helper.GetServicesManager().CreateProxy<IFileFieldManager>(ExecutionIdentity.System))
            {
                try
                {
                    logger.LogDebug("Hotfix: Creating Destination Folder {path}", PackageDestinationFolderPath);
                    System.IO.Directory.CreateDirectory(PackageDestinationFolderPath);
                    using (FileStream file = File.Open(PackageDestinationFilePath, FileMode.Create))
                    {
                        Task<IKeplerStream> stream = proxy.DownloadAsync(Helper.GetActiveCaseID(), Target, FileField);
                        stream.Result.GetStreamAsync().Result.CopyTo(file);
                        logger.LogDebug("Hotfix: Package File saved to {@Path}", propertyValues: PackageDestinationFilePath);
                        file.Close();
                        //remove previous unzipped folder if applicable
                        if (System.IO.Directory.Exists(PackageDestinationUnzippedPath))
                        {
                            System.IO.Directory.Delete(PackageDestinationUnzippedPath, true);
                        }
                        //extract zip to expanded location
                        System.IO.Compression.ZipFile.ExtractToDirectory(PackageDestinationFilePath, PackageDestinationUnzippedPath);
                        logger.LogDebug("Hotfix: Package File unzipped to {@Path}", propertyValues: PackageDestinationUnzippedPath);                                           
                    }

                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Exception while saving hotfix package");
                    throw;
                }
            }
            //update disk location field with location
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                try
                {
                    var currentObject = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID };
                    var diskLocFVP = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Guid = Constants.Constants.DISK_LOCATION_FIELD },
                        Value = PackageDestinationFolderPath
                    };                  
                    var updateRequest = new UpdateRequest
                    {
                        Object = currentObject,
                        FieldValues = new List<FieldRefValuePair> { diskLocFVP }
                    };

                    objectManager.UpdateAsync(Helper.GetActiveCaseID(), updateRequest).Wait();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Exception while updating package disk location.");
                    throw;
                }
            }
        } //end downloadAndSaveToDisk

        //this opens the master manifest and populates metadata onto the RDO.
        private void parsePackgeLevelXml()
        {
            XElement xname = new XElement("Name", "PREINIT");
            XElement xversion = new XElement("Version", "PREINIT");
            try
            {   //load master manifest as XML and call ObjectManager to update fields.                
                XDocument xml = XDocument.Load(PackageDestinationUnzippedPath + @"\MasterManifest.xml");
                xname = xml.Descendants().FirstOrDefault(i => i.Name.LocalName == "Name");
                xversion = xml.Descendants().FirstOrDefault(i => i.Name.LocalName == "Version");
                if (xname is null)
                {
                    throw new NullReferenceException("Failed to locate Name property in master manifest. NULL value was retrieved instead.");
                }            
                if (xversion is null)
                {
                    throw new NullReferenceException("Failed to locate Version property in master manifest. NULL value was retrieved instead.");
                }            
                                
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Exception while opening master manifest");
                throw;
            }

            //write captured values out to object
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy <IObjectManager>(ExecutionIdentity.System))
            {
                try
                {
                    var currentObject = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID };
                    var nameFVP = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Guid = Constants.Constants.NAME_FIELD },
                        Value = xname.Value.ToString()
                    };
                    var versionFVP = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Guid = Constants.Constants.VERSION_FIELD },
                        Value = xversion.Value.ToString()
                    };
                    var updateRequest = new UpdateRequest
                    {
                        Object = currentObject,
                        FieldValues = new List<FieldRefValuePair> { nameFVP, versionFVP }
                    };

                    objectManager.UpdateAsync(Helper.GetActiveCaseID(), updateRequest).Wait();                   
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Exception while updating package fields with XML data.");
                    throw;
                }
            }
        } //end parsePackageLevelXml

        /// <summary>
        ///     The RequiredFields property tells Relativity that your event handler needs to have access to specific fields that
        ///     you return in this collection property
        ///     regardless if they are on the current layout or not. These fields will be returned in the ActiveArtifact.Fields
        ///     collection just like other fields that are on
        ///     the current layout when the event handler is executed.
        /// </summary>
        public override FieldCollection RequiredFields
        {
            get
            {
                kCura.EventHandler.FieldCollection retVal = new kCura.EventHandler.FieldCollection();

                return retVal;
            }
        } //end RequiredFields
    }
}