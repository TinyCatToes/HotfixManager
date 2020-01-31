using System;
using System.Net;
using System.Runtime.InteropServices;
using kCura.EventHandler;
using kCura.EventHandler.CustomAttributes;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;
using Relativity.Services.Objects.DataContracts;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace HotfixManager.EventHandlers
{
    [kCura.EventHandler.CustomAttributes.Description("Post Install Event Handler to build out necessary objects and fields")]
    [System.Runtime.InteropServices.Guid("B3BFEE85-DBAA-48BF-8B1B-BD8BCC452DF8")]
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