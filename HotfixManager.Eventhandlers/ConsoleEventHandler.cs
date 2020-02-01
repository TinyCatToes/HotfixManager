using System;
using System.Collections.Generic;
using System.Net;
using System.Data;
using System.Runtime.InteropServices;
using kCura.EventHandler;
using kCura.EventHandler.CustomAttributes;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;
using System.Data.SqlClient;
using Relativity.Services.Objects.DataContracts;

namespace HotfixManager.EventHandlers
{
    [kCura.EventHandler.CustomAttributes.Description("Console Event Handler to add queue control buttons to hotfix layout")]
    [System.Runtime.InteropServices.Guid("A4979CE9-8640-4D15-B635-99D86F776121")]
    public class HotfixConsoleEventhandler : kCura.EventHandler.ConsoleEventHandler
    {
        private const string JOB_EXISTS_QUERY = @"select case when not exists (select 1 from EDDS.eddsdbo.HotfixDeployQueue where PackageArtifactID = @PackageArtifactID) then 0 else (select Status from EDDS.eddsdbo.HotfixDeployQueue where PackageArtifactID = @PackageArtifactID) end";
        private const string ENQUEUE_QUERY = @"insert into EDDS.eddsdbo.HotfixDeployQueue values (@PackageArtifactID,@WorkspaceArtifactID,1,'',100,GETUTCDATE(),@ActingUser)";
        private const string DEQUEUE_QUERY = @"delete from EDDS.eddsdbo.HotfixDeployQueue where PackageArtifactID = @PackageArtifactID";
        private const string RETRY_ERROR_QUERY = @"update EDDS.eddsdbo.HotfixDeployQueue set Status = 4 where PackageArtifactID = @PackageArtifactID";
        private IAPILog logger;
       
        public override kCura.EventHandler.Console GetConsole(PageEvent pageEvent)
        {
            // security protocol and logging setup
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            logger = Helper.GetLoggerFactory().GetLogger().ForContext<HotfixConsoleEventhandler>();

            //create console
            kCura.EventHandler.Console hotfixConsole = new kCura.EventHandler.Console()
            { Items = new List<IConsoleItem>(), Title = "Manage Package" };

            //create buttons
            ConsoleButton enqueueButton = new ConsoleButton() { Name = "AddToDeployQueue", DisplayText = "Queue Deployment", ToolTip = "Queues the package for deployment", Enabled = true, RaisesPostBack = true };
            ConsoleButton dequeueButton = new ConsoleButton() { Name = "RemoveFromDeployQueue", DisplayText = "Cancel Deployment", ToolTip = "Removes the package from the deployment queue", Enabled = false, RaisesPostBack = true };
            ConsoleButton errorRetryButton = new ConsoleButton() { Name = "QueueErrorRetry", DisplayText = "Retry Deploy Errors", ToolTip = "Attempts to retry errors encountered during deployment", Enabled = false, RaisesPostBack = true };

            //check if job is is in the queue table. returns 0 if it is not, returns current status if it is.
            if (pageEvent == PageEvent.PreRender)
            {
                SqlParameter selfArtifactID = new SqlParameter("PackageArtifactID", SqlDbType.Int);
                selfArtifactID.Value = this.ActiveArtifact.ArtifactID;
                int queueStatus = Helper.GetDBContext(-1).ExecuteSqlStatementAsScalar<Int32>(JOB_EXISTS_QUERY, selfArtifactID);
                if (queueStatus > 0)
                {
                    enqueueButton.Enabled = false;
                    dequeueButton.Enabled = true;
                    dequeueButton.StyleAttribute = "background:maroon !important";                    
                }
                if (queueStatus == 3)
                { 
                        errorRetryButton.Enabled = true;
                }
            }

            //add buttons to the console
            hotfixConsole.Items.Add(enqueueButton);
            hotfixConsole.Items.Add(dequeueButton);
            hotfixConsole.Items.Add(errorRetryButton);
            return hotfixConsole;
        }

        public override void OnButtonClick(ConsoleButton consoleButton)
        {
            // Update Security Protocol
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            IAPILog logger = Helper.GetLoggerFactory().GetLogger().ForContext<HotfixConsoleEventhandler>();
            //build param   
            SqlParameter selfArtifactID = new SqlParameter("PackageArtifactID", SqlDbType.Int);
            selfArtifactID.Value = this.ActiveArtifact.ArtifactID;

            switch (consoleButton.Name)
            {
                case "AddToDeployQueue":
                    enqueueJob(selfArtifactID);
                    break;
                case "RemoveFromDeployQueue":
                    dequeueJob(selfArtifactID);
                    break;
                case "QueueErrorRetry":
                    retryError(selfArtifactID);
                    break;
                default:
                    break;
                        
            }
        } //end OnButtonClick       

        private void enqueueJob(SqlParameter selfArtifactID)
        {
            try
            {
                SqlParameter actingUser = new SqlParameter("ActingUser", SqlDbType.Int);
                SqlParameter workspaceArtifactID = new SqlParameter("WorkspaceArtifactID", SqlDbType.Int);
                actingUser.Value = Helper.GetAuthenticationManager().UserInfo.ArtifactID;
                workspaceArtifactID.Value = Helper.GetActiveCaseID();
                Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(ENQUEUE_QUERY, new List<SqlParameter> { selfArtifactID, workspaceArtifactID, actingUser } ); 
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error attempting to enqueue job.");
                return;
            }

            //after successfully adding queue row, update job.
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var currentObject = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID };
                var statusFVP =  new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_STATUS_FIELD },
                    Value = new ChoiceRef() { Guid = Constants.Constants.LAST_RUN_QUEUED_CHOICE }
                };
                var timeFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_TIME_FIELD },
                    Value = DateTime.Now
                };
                var updateRequest = new UpdateRequest
                {
                    Object = currentObject,
                    FieldValues = new List<FieldRefValuePair> { statusFVP, timeFVP }
                };
                try
                {
                    var objManResult = objectManager.UpdateAsync(Helper.GetActiveCaseID(), updateRequest).Result;
                }
                catch (AggregateException ex)
                {//print error for each exception in aggregate, then end.
                    foreach (var exchild in ex.InnerExceptions)
                    {
                        logger.LogError(exchild, "Hotfix: Failed to update object {artID} with queueing status.", selfArtifactID.Value.ToString());
                    }
                    return;
                }
            }
        } //end enqueueJob
                
        private void dequeueJob(SqlParameter selfArtifactID)
        {
            try
            {
                int result = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(DEQUEUE_QUERY, new SqlParameter[] { selfArtifactID }, 90);
                if (result < 1)
                {
                    logger.LogError("Hotfix: Failed to Dequeue job {jobArtID}, Queue row not found", selfArtifactID.Value.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error attemptingt to dequeue job.");
            }

            //after successfully cancelling job, update job with cancelled status
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var currentObject = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID };
                var statusFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_STATUS_FIELD },
                    Value = new ChoiceRef() { Guid = Constants.Constants.LAST_RUN_CANCELLED_CHOICE }
                };
                var updateRequest = new UpdateRequest
                {
                    Object = currentObject,
                    FieldValues = new List<FieldRefValuePair> { statusFVP }
                };
                try 
                {
                    var objManResult = objectManager.UpdateAsync(Helper.GetActiveCaseID(), updateRequest).Result;
                }
                catch (AggregateException ex)
                {//print error for each exception in aggregate, then end.
                    foreach (var exchild in ex.InnerExceptions)
                    {
                        logger.LogError(exchild, "Hotfix: Failed to update object {artID} with queueing status.", selfArtifactID.Value.ToString());
                    }
                    return;
                }
            }

        }
        
        private void retryError(SqlParameter selfArtifactID)
        {
            try
            {
                int result = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(RETRY_ERROR_QUERY, new SqlParameter[] { selfArtifactID }, 90);
                if (result < 1)
                {
                    logger.LogError("Hotfix: Failed to retry job {jobArtID}, Queue row not found", selfArtifactID.Value.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error attempting to retry job.");
            }

            //after successfully adding queue row, update job.
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var currentObject = new RelativityObjectRef { ArtifactID = this.ActiveArtifact.ArtifactID };
                var statusFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_STATUS_FIELD },
                    Value = new ChoiceRef() { Guid = Constants.Constants.LAST_RUN_QUEUED_CHOICE }
                };
                var timeFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_TIME_FIELD },
                    Value = DateTime.Now
                };
                var updateRequest = new UpdateRequest
                {
                    Object = currentObject,
                    FieldValues = new List<FieldRefValuePair> { statusFVP, timeFVP }
                };
                try
                {
                    var objManResult = objectManager.UpdateAsync(Helper.GetActiveCaseID(), updateRequest).Result;
                }
                catch (AggregateException ex)
                {//print error for each exception in aggregate, then end.
                    foreach (var exchild in ex.InnerExceptions)
                    {
                        logger.LogError(exchild, "Hotfix: Failed to update object {artID} with queueing status.", selfArtifactID.Value.ToString());
                    }
                    return;
                }
                catch(Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Failed to update object {artID} with queueing status.", selfArtifactID.Value.ToString());
                }
            }
        }

        private enum StatusCode
        {
            Queued,
            InProgress,
            Error,
            RetryQueued
        }

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
        }
    }
}