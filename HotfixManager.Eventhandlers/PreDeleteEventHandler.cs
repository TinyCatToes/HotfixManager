using System;
using System.Net;
using System.IO;
using System.Runtime.InteropServices;
using kCura.EventHandler;
using kCura.EventHandler.CustomAttributes;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;
using Relativity.Services.Objects.DataContracts;
using System.Threading.Tasks;

namespace HotfixManager.EventHandlers
{
    [kCura.EventHandler.CustomAttributes.Description("Pre-Delete event handler that removes hotfix package files from the fileshare.")]
    [System.Runtime.InteropServices.Guid("38ECF8D8-55E0-449E-B1BF-A38956245776")]
    public class HotfixCleanupDeleteHandler : kCura.EventHandler.PreDeleteEventHandler
    {        
        public override Response Execute()
        {
            // Update Security Protocol
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            //Construct a response object with default values.
            kCura.EventHandler.Response retVal = new kCura.EventHandler.Response();
            retVal.Success = true;
            retVal.Message = string.Empty;
            IAPILog logger = Helper.GetLoggerFactory().GetLogger().ForContext<HotfixCleanupDeleteHandler>();
            string folderLocation = "PReINIT";
            Relativity.Services.Objects.DataContracts.ReadResult result;
            try
            {   //get current managed file location
                using(IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
                {
                    try
                    {
                        FieldRef folderLocationRef = new FieldRef { Guid = Constants.Constants.DISK_LOCATION_FIELD };
                        var readRequest = new ReadRequest
                        {
                            Object = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID },
                            Fields = new FieldRef[] { folderLocationRef }
                        };
                        result = objectManager.ReadAsync(-1, readRequest).Result;                        
                    }
                    catch (AggregateException ex)
                    {
                        foreach (var inex in ex.InnerExceptions)
                        {
                            logger.LogError(inex, "Hotfix: Exception when getting package location for delete.");
                        }
                        retVal.Success = false;
                        retVal.Message = ex.ToString();
                        return retVal;
                    }
                }

                folderLocation = result.Object.FieldValues[0].Value.ToString();
              
                logger.LogVerbose("Hotfix: Got managed folder path: {path}", folderLocation);

                //check if folder exists. If it does not, fall through to completion
                if(System.IO.Directory.Exists(folderLocation))
                {//if it does, delete recursively                    
                    System.IO.Directory.Delete(folderLocation, true);
                    logger.LogVerbose("Hotfix: Deleted managed folder at {path}", folderLocation);
                }
                else
                {
                    logger.LogVerbose("Hofix: No folder detected at {path}, skipping delete.", folderLocation);
                }
            }
            catch (Exception ex)
            {                
                retVal.Success = false;
                retVal.Message = ex.ToString();
                logger.LogError(ex, "Hotfix: Failed to delete managed folder at {path}", folderLocation);
            }
            return retVal;

            //TODO
            //also remove any queued job rows

        } //end Execute
        
        public override void Rollback()
        {
        } //do nothing

        public override void Commit()
        {
        } //do nothing
        
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
                return null;
            }
        }
    }
}