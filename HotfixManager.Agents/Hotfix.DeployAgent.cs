using kCura.Agent;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;
using Relativity.Services.Objects.DataContracts;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;

namespace HotfixManager.Agents
{
    [kCura.Agent.CustomAttributes.Name("Hotfix Deployment Agent")]
    [System.Runtime.InteropServices.Guid("D7B19644-7C7C-4D60-A3B6-7E15CE0D8F30")]
    public class DeployAgent : AgentBase
    {
        private static string CHECK_QUEUE_NEW_QUERY = @"select TOP 1 * from EDDS.eddsdbo.HotfixDeployQueue where (AgentName is null) OR (AgentName = '') and [Status] = 1 order by QueueID asc";
        private static string CHECK_QUEUE_EXISTING_QUERY = @"select TOP 1 * from EDDS.eddsdbo.HotfixDeployQueue where AgentName = @AgentName and [Status] = 4 order by QueueID asc";
        private static string LOCKQUEUEENTRY_QUERY = @"update EDDS.eddsdbo.HotfixDeployQueue set AgentName = @AgentName, Status = 2 where QueueID = @QueueID and PackageArtifactID = @PackageArtifactID and Status = 1 and ((AgentName is null) or (AgentName = ''))";
        private static string DELETEFROMQUEUE_QUERY = @"delete from EDDS.eddsdbo.HotfixDeployQueue where QueueID = @QueueID and PackageArtifactID = @PackageArtifactID";
        private static string SET_STATUS_ERROR_QUERY = @"update EDDS.eddsdbo.HotfixDeployQueue set Status = 3 where packageArtifactID = @PackageArtifactID and QueueID = @QueueID";
        private int packageArtifactID = 0;
        private int queueID = 0;
        private int workspaceArtifactID = 0;
        private bool slapdashAllInOneOverride = true; //remove this once the selfdrop problem is solved.

        //private string packageDiskLocation = "PREINIT";
        private IAPILog logger;

        public override void Execute()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            logger = Helper.GetLoggerFactory().GetLogger().ForContext<DeployAgent>();

            //check if current time is during offhours.             
            if (!IsOffHours())
            {
                RaiseMessage("Current time is not during off-hours range.", 10);
                return;
            }

            //get queue row from database            
            try
            {
                DataRow queuerow = getQueuedJobRowFromTable();
                if (queuerow == null)
                {
                    return; //no row found, exit
                }
                queueID = queuerow.Field<int>("QueueID");
                packageArtifactID = queuerow.Field<int>("PackageArtifactID");
                workspaceArtifactID = queuerow.Field<int>("WorkspaceArtifactID");
                logger.LogVerbose("Hotfix: Located deloyable package {AID} with queueID {QID}", packageArtifactID, queueID);//verb
            }
            catch (Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Failed to read from queue table.", ex.ToString());
            }

            //lock that row 
            try
            {
                int lockresult = lockQueueRowforActiveJob(packageArtifactID, queueID);
                if (lockresult != 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Failed to lock queue row for work.", ex.ToString());                
            }

            //create logging RDO
            RelativityObject logRDO = new RelativityObject() { ArtifactID = 0 };
            try
            {
                logRDO = createLogRDO();                
            }
            catch(Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Failed to create Job History RDO", ex.ToString());                
            }

            List<string> serverList = new List<string>();
            //generate list of target servers.
            try
            {
                
                serverList = getServerList(logRDO);
            }
            catch(Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Failed to read server list.", ex.ToString());
            }
            
            //loop the list and call deploy.
            foreach (string curServer in serverList)
            {
                try
                {
                    deployToServer(curServer);
                }
                catch (Exception ex)
                {                    
                    exitWithFailure(ex.ToString());
                    RaiseError("Failed to deploy to server" + curServer,ex.ToString());
                }
            }
            exitWithSuccess(logRDO);

        }//end Execute

        //checks the queue table for rows that are available for deployment. If no rows are returned or the read errors, returns null
        private DataRow getQueuedJobRowFromTable()
        {
            //look for an errored queue row already locked by this agent.
            DataTable ExistingQResult = new DataTable();
            SqlParameter agentNameParam = new SqlParameter("AgentName", SqlDbType.Int);
            agentNameParam.Value = this.AgentID;
            try
            {
                ExistingQResult = Helper.GetDBContext(-1).ExecuteSqlStatementAsDataTable(CHECK_QUEUE_EXISTING_QUERY, new List<SqlParameter> { agentNameParam } );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Failed to read from HotfixDeployQueue.");
                throw;
            }
            //if found, return our existing errored row.
            if(ExistingQResult.Rows.Count > 0)
            {
                return ExistingQResult.Rows[0];
            }

            //check for unlocked queue rows
            DataTable NewQResult = new DataTable();            
            try
            {
                NewQResult = Helper.GetDBContext(-1).ExecuteSqlStatementAsDataTable(CHECK_QUEUE_NEW_QUERY);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Failed to read from HotfixDeployQueue.");
                throw;
            }

            //check if datatable is empty. if no rows returned, exit.
            if (NewQResult == null || NewQResult.Rows.Count == 0)
            {
                logger.LogVerbose("Hotfix: No packages ready to deploy found. Exiting.");//verb
                RaiseMessage("No packages to deploy.", 10);
                return null;
            }
            else
            {
                return NewQResult.Rows[0];
            }                           
        } //end getQueuedJobRowFromTable

        //updates a row in the queue table to lock it while we work. returns 1 for nonsuccess, and 0 for success.       
        private int lockQueueRowforActiveJob(int packageArtifactID, int QueueID)
        {

            SqlParameter packageIDParam = new SqlParameter("PackageArtifactID", SqlDbType.Int);
            SqlParameter queueIDParam = new SqlParameter("QueueID", SqlDbType.Int);
            SqlParameter agentNameParam = new SqlParameter("AgentName", SqlDbType.Int);
            packageIDParam.Value = packageArtifactID;
            queueIDParam.Value = queueID;
            agentNameParam.Value = this.AgentID;
            try
            {
                int rowsaffected = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(LOCKQUEUEENTRY_QUERY, new List<SqlParameter> { packageIDParam, queueIDParam, agentNameParam });
                if (rowsaffected == 0)
                {
                    logger.LogVerbose("Hotfix: Lost race to lock queue row #{QID} for package {PID}. Exiting.", queueID, packageArtifactID);//verb
                    return 1;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Failed to lock deploy queue row.");
                throw;
            }
            RaiseMessage("Beginning deploy of package " + packageArtifactID.ToString(), 10);
            updateInlineFieldsWithResult("In Progress");
            return 0;
        } //end lockQueueRowforActiveJob

        //this writes deploy queue status back to the RDO
        //does not rethrow exceptions (flow can continue if this method fails)
        private void updateInlineFieldsWithResult(string status, string Message = "")
        {
            var deployStatusFVP = new FieldRefValuePair
            {
                Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_STATUS_FIELD },
            };
            switch (status)
            {
                case "In Progress":
                    deployStatusFVP.Value = new ChoiceRef { Guid = Constants.Constants.LAST_RUN_INPROG_CHOICE };
                    break;
                case "Error":
                    deployStatusFVP.Value = new ChoiceRef { Guid = Constants.Constants.LAST_RUN_ERROR_CHOICE };
                    break;
                case "Complete":
                    deployStatusFVP.Value = new ChoiceRef { Guid = Constants.Constants.LAST_RUN_COMPLETE_CHOICE };
                    break;
                default:
                    logger.LogError("Hotfix: Undefined Last Run Status Entered.");
                    RaiseError("Undefined Last Run Status Entered.","Invalid Status code entered: "+status);
                    break;
            };

            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var CurrentObject = new RelativityObjectRef { ArtifactID = packageArtifactID };                
                var deployErrorFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.LAST_RUN_ERROR_FIELD },
                    Value = Message
                };

                var updateRequest = new UpdateRequest
                {
                    Object = CurrentObject,
                    FieldValues = new List<FieldRefValuePair> { deployStatusFVP, deployErrorFVP }
                };
                try
                {
                    var objManResult = objectManager.UpdateAsync(workspaceArtifactID, updateRequest).Result;
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
      
        //creates and initializes the RDO that holds all logging information. 
        //returns a RelativityObject representing the log RDO.
        private RelativityObject createLogRDO()
        {
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {                                
                try
                {
                    var logName = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Name = "Name" },
                        Value = packageArtifactID.ToString() + DateTime.UtcNow.ToString()
                    };             
                    var logRunDate = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Name = "Run Time" },
                        Value = DateTime.Now.ToString()
                    };                    
                    var logRunStatus = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Guid = Constants.Constants.LOG_STATUS_FIELD },
                        Value = new ChoiceRef { Guid = Constants.Constants.LOG_STATUS_INPROG_CHOICE }
                    };                    
                    var logLongText = new FieldRefValuePair
                    {
                        Field = new FieldRef() { Name = "Log" },
                        Value = @"***Beginning deployment of package " + packageArtifactID.ToString() + " by agent " + this.AgentID + "***"
                    };                    
                    CreateRequest createreq = new CreateRequest
                    {
                        ObjectType = new ObjectTypeRef() { Name = "Hotfix - Job History"},
                        ParentObject = new RelativityObjectRef() { ArtifactID = packageArtifactID },
                        FieldValues = new List<FieldRefValuePair> { logName,logRunDate,logRunStatus,logLongText}
                    };                    
                    var result = objectManager.CreateAsync(workspaceArtifactID, createreq).Result;                  
                    return result.Object;
                }
                catch(AggregateException ex)
                {
                    foreach(Exception e in ex.InnerExceptions)
                    {                     
                        logger.LogError(e, "Hotfix: failed to create deploy log object.");
                    }
                    throw;
                }
                catch(Exception ex)
                {                    
                    logger.LogError(ex, "Hotfix: failed to create deploy log object.");
                    throw;
                }                             
            }            
        }  //end createLogRDO

        private List<string> getServerList(RelativityObject logRDO)
        {
            Relativity.Services.Objects.DataContracts.QueryResult qresult;
            var retList = new List<string>();
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                QueryRequest qrequest = new QueryRequest
                {
                    ObjectType = new ObjectTypeRef() { Name = "Resource Server" },
                    Condition = "(('Type' IN  ['Web','Agent','Worker Manager']))",
                    Fields = new List<FieldRef>()
                    {
                        new FieldRef() { Name = "ArtifactID" },
                        new FieldRef() { Name = "Name" },
                        new FieldRef() { Name = "Type" }
                    }                    
                };

                try
                {//query admin DB for servers of desired type
                    qresult = objectManager.QueryAsync(-1, qrequest, 1, 150).Result;
                }
                catch(AggregateException ex)
                {
                    foreach (Exception e in ex.InnerExceptions)
                    {
                        logger.LogError(e, "Hotfix: failed to get server list.");
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,"Hotfix: failed to get server list.");
                    throw;
                }
                foreach(RelativityObject obj in qresult.Objects )
                {
                    string message = "Located " + obj.FieldValues[2].Value.ToString() + " server " + obj.FieldValues[1].Value.ToString() + " with artifactID " + obj.FieldValues[0].Value.ToString();
                    writeToLog(message,logRDO);
                    if ((int)obj.FieldValues[0].Value == this.GetAgentServerID() && slapdashAllInOneOverride is false)//check to see if self is in list. added terrible override key to allow for testing in all-in-ones
                    {
                        writeToLog("Skipping server " + obj.FieldValues[1].Value.ToString() + "because an agent cannot deploy a package to its own server.",logRDO);
                    }
                    else
                    {//add current server to list ONLY if it is not the current server and not already in the list.
                        if (!retList.Contains(obj.FieldValues[1].Value.ToString()) )
                        {
                            retList.Add(obj.FieldValues[1].Value.ToString());
                            writeToLog("Queued drop to " + obj.FieldValues[2].Value.ToString() + " server " + obj.FieldValues[1].Value.ToString(), logRDO);
                        }
                        else
                        {
                            writeToLog("Skipped drop to " + obj.FieldValues[2].Value.ToString() + " server " + obj.FieldValues[1].Value.ToString() + " as this server is already queued", logRDO);
                        }
                    }
                }
                return retList;
            }
        } //getServerList

        //full deploy action for a single server
        //backs up the files to C:\<timestamp>-<packageArtifactID>-OriginalDLLs
        //TODO implement package type to allow for INV and REL deploy types.
        private void deployToServer(string serverName)
        {

        }

        //wrapper to append lines to the log RDO. 
        //does not rethrow exceptions, simply logs them.
        private void writeToLog(string logMessage, RelativityObject logRDO )
        {            

            //read current log data.
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {               
                Relativity.Services.Objects.DataContracts.ReadResult readResult;
                ReadRequest readreq = new ReadRequest()
                {
                    Object = new RelativityObjectRef() { ArtifactID = logRDO.ArtifactID },
                    Fields = new List<FieldRef>()
                    {
                        new FieldRef() {Name = "Log"}
                    }
                };             
                try
                {
                    readResult = objectManager.ReadAsync(workspaceArtifactID, readreq).Result;
                    if (readResult.Object.FieldValues.Count < 1)
                    {
                        throw new Exception("ReadAsync returned no results.");
                    }
                }
                catch (AggregateException ex)
                {
                    foreach (Exception e in ex.InnerExceptions)
                    {
                        logger.LogError(e, "Hotfix: failed to read current job log.");
                    }
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: failed to read current job log.");
                    throw;
                }                                

                try
                {                 
                    string currentLog = readResult.Object.FieldValues[0].Value.ToString();
                    string appendedLog = currentLog + Environment.NewLine + logMessage;                    

                    var updateRequest = new UpdateRequest()
                    {
                        Object = new RelativityObjectRef() { ArtifactID = logRDO.ArtifactID },
                        FieldValues = new List<FieldRefValuePair>()
                        {
                            new FieldRefValuePair()
                            {
                                Field = new FieldRef() {Name = "Log"},
                                Value = appendedLog
                            }
                        }
                    };
                    logger.LogFatal("Attempting UpdateAsync");
                
                
                        var updresult = objectManager.UpdateAsync(workspaceArtifactID, updateRequest).Result;
                    }
                    catch (AggregateException ex)
                    {//print error for each exception in aggregate, then end.
                        foreach (var exchild in ex.InnerExceptions)
                        {
                            logger.LogError(exchild, "Hotfix: Error when writing to log object.");
                        }
                        return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Error when writing to log object.");
                    return;
                }
            }
        } //end writeToLog

        //removes queue row and writes to log and inline results fields
        private void exitWithSuccess(RelativityObject logRDO)
        {
            //remove job row from queue
            SqlParameter packageIDParam = new SqlParameter("PackageArtifactID", SqlDbType.Int);
            SqlParameter queueIDParam = new SqlParameter("QueueID", SqlDbType.Int);
            packageIDParam.Value = packageArtifactID;
            queueIDParam.Value = queueID;
            try
            {
                int rowsaffected = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(DELETEFROMQUEUE_QUERY, new List<SqlParameter> { packageIDParam, queueIDParam });
                if (rowsaffected == 0)
                {
                    throw new Exception("No row found to delete for package ID" + packageArtifactID.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error Removing row from Deploy queue");
            }

            //update logRDO status.
            updateInlineFieldsWithResult("Complete");
            writeToLog("***Completed Deployment of package.***",logRDO);

            try
            {
                var updReq = new UpdateRequest()
                {
                    Object = new RelativityObjectRef() { ArtifactID = logRDO.ArtifactID },
                    FieldValues = new List<FieldRefValuePair>()
                    {
                        new FieldRefValuePair()
                        {
                            Field = new FieldRef()
                            {
                                Name = "Run Status"
                            },
                            Value = new ChoiceRef()
                            {
                                Guid = Constants.Constants.LOG_STATUS_COMPLETE_CHOICE
                            }
                        }

                    }
                };
                using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
                {
                    var updresult = objectManager.UpdateAsync(workspaceArtifactID, updReq).Result;
                }

            }
            catch (AggregateException ex)
            {
                foreach (Exception exchild in ex.InnerExceptions)
                {
                    logger.LogError(exchild, "Hotfix: Error updating status of log RDO");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error updating status of log RDO");
            }

            RaiseMessage("Completed deploy for package " + packageArtifactID.ToString(), 10);
        }//end exitWithSuccess

        //method to set queue row to errored state and call updateInlineFieldsWithResult
        private void exitWithFailure(string message, RelativityObject logRDO = null)
        {
            //remove job row from queue
            SqlParameter packageIDParam = new SqlParameter("PackageArtifactID", SqlDbType.Int);
            SqlParameter queueIDParam = new SqlParameter("QueueID", SqlDbType.Int);
            packageIDParam.Value = packageArtifactID;
            queueIDParam.Value = queueID;
            try
            {
                int rowsaffected = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(SET_STATUS_ERROR_QUERY, new List<SqlParameter> { packageIDParam, queueIDParam });
                if (rowsaffected == 0)
                {
                    throw new Exception("No row found to set to Error for package ID" + packageArtifactID.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error setting row to Error in Deploy queue");
            }

            //update package RDO with results
            updateInlineFieldsWithResult("Error", message);

            //append to the log file, if it exsits. if it doesn't, just exit.
            if (logRDO is null)
            {
                logger.LogFatal("Hotfix: Log RDO doesn't exist, skipping write.");//verb
                return;
            }
            else
            {
                //update log RDO with message and Error status
                writeToLog(message, logRDO);
                try
                {
                    var updReq = new UpdateRequest()
                    {
                        Object = new RelativityObjectRef() { ArtifactID = logRDO.ArtifactID },
                        FieldValues = new List<FieldRefValuePair>()
                    {
                        new FieldRefValuePair()
                        {
                            Field = new FieldRef()
                            {
                                Name = "Run Status"
                            },
                            Value = new ChoiceRef()
                            {
                                Guid = Constants.Constants.LOG_STATUS_ERROR_CHOICE
                            }
                        }

                    }
                    };
                    using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
                    {
                        var updresult = objectManager.UpdateAsync(workspaceArtifactID, updReq).Result;
                    }

                }
                catch (AggregateException ex)
                {
                    foreach (Exception exchild in ex.InnerExceptions)
                    {
                        logger.LogError(exchild, "Hotfix: Error updating status of log RDO");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Error updating status of log RDO");
                }
            }
        }//end exitWithFailure

        private enum StatusCode
        {
            Queued,
            InProgress,
            Error,
            RetryQueued
        }

        /// <summary>
        /// Returns the name of agent
        /// </summary>
        public override string Name
        {
            get
            {
                return "Hotfix Deployment Agent";
            }
        }
    }
}