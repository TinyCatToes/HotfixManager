using System;
using System.Net;
using System.Runtime.InteropServices;
using kCura.EventHandler;
using kCura.EventHandler.CustomAttributes;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;

namespace HotfixManager.EventHandlers
{
    [kCura.EventHandler.CustomAttributes.Description("Post Install EventHandler")]
    [System.Runtime.InteropServices.Guid("1d932493-4ea0-471d-8baa-b64576720bf6")]
    public class PostInstallSetup : kCura.EventHandler.PostInstallEventHandler
    {
        private IAPILog logger;

        public override Response Execute()
        {
            // Update Security Protocol
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            IAPILog logger = Helper.GetLoggerFactory().GetLogger().ForContext<PostInstallSetup>();

            //Construct a response object with default values.
            kCura.EventHandler.Response retVal = new kCura.EventHandler.Response();
            retVal.Success = true;
            retVal.Message = string.Empty;
            //Get a dbContext for the EDDS database
            Relativity.API.IDBContext eddsDBContext = this.Helper.GetDBContext(-1);

            try
            {
                
            }
            catch (Exception ex)
            {
                //Change the response Success property to false to let the user know an error occurred
                retVal.Success = false;
                retVal.Message = ex.ToString();
            }

            return retVal;
        }//end Execute
    }
}