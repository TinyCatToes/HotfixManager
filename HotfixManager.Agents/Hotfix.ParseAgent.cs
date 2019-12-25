using kCura.Agent;
using kCura.Relativity.Client;
using Relativity.API;
using Relativity.Services.Objects;
using System;
using System.Net;
using System.Data;
using System.Data.SqlClient;
using System.Collections.Generic;
using Relativity.Services.Objects.DataContracts;
using System.Xml.Linq;
using System.Linq;
using Newtonsoft.Json;

namespace HotfixManager.Agents
{
    [kCura.Agent.CustomAttributes.Name("Hotfix Parse Agent")]
    [System.Runtime.InteropServices.Guid("67188fc9-e8c1-4974-91a1-64a78b2518ab")]        
    public class ParseAgent : AgentBase
    {
        private static string CHECK_QUEUE_NEW_QUERY = @"select TOP 1 * from EDDS.eddsdbo.HotfixParseQueue where (AgentName is null) OR (AgentName = '') and [Status] = 1 order by QueueID asc";
        private static string CHECK_QUEUE_EXISTING_QUERY = @"select TOP 1 * from EDDS.eddsdbo.HotfixParseQueue where AgentName = @AgentName and [Status] = 3 order by QueueID asc";
        private static string LOCK_QUEUE_ENTRY_QUERY = @"update EDDS.eddsdbo.HotfixParseQueue set AgentName = @AgentName, Status = 2 where QueueID = @QueueID and PackageArtifactID = @PackageArtifactID and Status = 1 and ((AgentName is null) or (AgentName = ''))";
        private static string DELETE_FROM_QUEUE_QUERY = @"delete from EDDS.eddsdbo.HotfixParseQueue where QueueID = @QueueID and PackageArtifactID = @PackageArtifactID";
        private static string SET_STATUS_ERROR_QUERY = @"update EDDS.eddsdbo.HotfixParseQueue set Status = 3 where AgentName = @AgentName and QueueID = @QueueID";
        private int packageArtifactID = 0;
        private int queueID = 0;
        private string packageDiskLocation = "PREINIT";
        private IAPILog logger;

        public override void Execute()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
            logger = Helper.GetLoggerFactory().GetLogger().ForContext<ParseAgent>();

            //get queue row from database
            try
            {                
                DataRow queuerow = getQueuedJobRowFromTable();
                if (queuerow == null)
                {
                    return; //nothing to do, let's get outta here.
                }
                queueID = queuerow.Field<int>("QueueID");
                packageArtifactID = queuerow.Field<int>("PackageArtifactID");
                logger.LogVerbose("Hotfix: Located parse-ready package {AID} with queueID {QID}", packageArtifactID, queueID);//verb
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
                    return; //lost race to lock, but does not require error reporting. exit and re-enter at top.
                }
            }
            catch (Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Failed to lock queue row for work.", ex.ToString());
            }

            //get package disk location for the locked job row.
            try
            {                
                packageDiskLocation = getDiskLocationFromPackage(packageArtifactID);
                if (packageDiskLocation is null)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Error querying for package disk location.", ex.ToString());
            }

            //open master manifest to check submanifests
            try
            {
                int workresult = openMasterManifest(packageDiskLocation);
                if (workresult == 0)
                {//work done, remove queue row and set to success.
                    logger.LogVerbose("Hotfix: Completed Parsing queue row {q} for package {p}. Exiting.", queueID, packageArtifactID);
                    exitWithSuccess();
                }
            }
            catch(Exception ex)
            {
                exitWithFailure(ex.ToString());
                RaiseError("Error parsing manifests.", ex.ToString());
            }
        }//end Execute
        
        //checks the queue table for rows that are available for parsing. If no rows are returned or the read errors, returns null
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
            if (ExistingQResult.Rows.Count > 0)
            {
                return ExistingQResult.Rows[0];
            }

            DataTable NewQResult = new DataTable();
            //get queue row from database.
            try
            {
                NewQResult = Helper.GetDBContext(-1).ExecuteSqlStatementAsDataTable(CHECK_QUEUE_NEW_QUERY);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Failed to read from HotfixParseQueue.");
                throw;
            }

            //check if datatable is empty. if no rows returned, exit.
            if (NewQResult == null || NewQResult.Rows.Count == 0)
            {
                logger.LogVerbose("Hotfix: No parse-ready packages found. Exiting.");//verb
                RaiseMessage("No packages to parse.", 10);
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
                int rowsaffected = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(LOCK_QUEUE_ENTRY_QUERY, new List<SqlParameter> { packageIDParam, queueIDParam, agentNameParam });
                if (rowsaffected == 0)
                {
                    logger.LogVerbose("Hotfix: Lost race to lock queue row #{QID} for package {PID}. Exiting.", queueID, packageArtifactID);//verb
                    return 1;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hotfix: Failed to lock parse queue row for processing.");
                throw;
            }
            RaiseMessage("Beginning parse of package " + packageArtifactID.ToString(), 10);
            updateInlineFieldsWithResult("In Progress");
            return 0;
        } //end lockQueueRowforActiveJob

        //uses ObjectManager to grab the package disk location from the package object. returns the path as a string, or a null if there is an error.
        private string getDiskLocationFromPackage(int packageArtifactID)                       
        {
            string resultValue = "PREINIT";
            string queryRequestString = "PREINIT";
            var queryRequest = new QueryRequest();            
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                try
                {                    
                queryRequest.ObjectType = new ObjectTypeRef() { Guid = Constants.Constants.HOTFIX_OBJECT_TYPE };
                queryRequest.Fields = new List<FieldRef>()
                {
                    new FieldRef() { Guid = Constants.Constants.DISK_LOCATION_FIELD }
                };                                    
                queryRequestString = @"(('ArtifactID' == '" + packageArtifactID.ToString() + @"'))";                                    
                queryRequest.Condition = queryRequestString;                           

                Relativity.Services.Objects.DataContracts.QueryResult qresult = objectManager.QueryAsync(-1, queryRequest, 1, 1).Result;
                resultValue = qresult.Objects[0].FieldValues[0].Value.ToString();
                logger.LogVerbose("Hotifx: Retrieved path {path} for package object {AID}", resultValue,packageArtifactID);
                }            
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hotfix: Error querying for Package Disk Location.");
                    throw;
                }               
            }
                return resultValue;
        }//end getDiskLocationFromPackage


        //reads the location of the server level drops from the master manifest. Calls parseSubManifest for each manifest found.
        private int openMasterManifest(string packageDiskLocation)
        {
            int retVal = 0;
            //load master manifest as XML
            string expandedPackageBaseLocation = packageDiskLocation + @"\Expanded\";
            string xlocation = expandedPackageBaseLocation + @"MasterManifest.xml";
            bool relativityDropExists = false;
            bool invariantDropExists = false;
            XDocument xml = new XDocument();
            XElement relativityPackageLocation = new XElement("RelativityPackage");
            XElement invariantPackageLocation = new XElement("InvariantPackage");
           try
            {
                xml = XDocument.Load(xlocation);
                logger.LogVerbose("Hotfix: Loaded XDocument from {xlocation}", xlocation);//verb
                relativityPackageLocation = xml.Descendants("DropItManifest").Where(t => t.Attribute("Type").Value == "Relativity").Elements("Location").FirstOrDefault();           
                if (relativityPackageLocation is null || relativityPackageLocation.IsEmpty)
                {
                    logger.LogVerbose("Hotfix: No Relativity submanifest detected.");//verb                    
                }
                else 
                {                    
                    relativityDropExists = true;
                    logger.LogVerbose("Hotfix: Detected Relativity submanifest at {path}", relativityPackageLocation.Value.ToString());//verb
                }                
                invariantPackageLocation = xml.Descendants("DropItManifest").Where(t => t.Attribute("Type").Value == "Invariant").Elements("Location").FirstOrDefault();                
                if (invariantPackageLocation is null || invariantPackageLocation.IsEmpty)
                {                    
                    logger.LogVerbose("Hotfix: No Invariant submanifest detected.");//verb
                }
                else
                {                    
                    invariantDropExists = true;
                    logger.LogVerbose("Hotfix: Detected Invariant submanifest at {path}", invariantPackageLocation.Value.ToString());//verb
                }                                              
                
            }
            catch (Exception ex)
            {
                logger.LogError(ex,"Hotfix: Error getting submanifests from master XML");
                retVal = 1;
                throw;
            }
            try
            {
                if (relativityDropExists == true)
                {
                    int relret = parseSubManifest(relativityPackageLocation.Value.Replace(@".\", expandedPackageBaseLocation.ToString()));
                    if (relret != 0)
                    {
                        retVal = 2;
                    }
                }
                if (invariantDropExists == true)
                {
                    int invret = parseSubManifest(invariantPackageLocation.Value.Replace(@".\", expandedPackageBaseLocation.ToString()));
                    if (invret != 0)
                    {
                        retVal = 2;
                    }
                }

            }
            catch(Exception)
            {
                throw;
            }
            return retVal;
        }//end openMasterManifest

        //parse the submanifests that DropIt use for the actual drop. Creates RDOs for each file with a list of locations. 
        //This is done only for reporting purposes. We don't change the manifest at all.
        private int parseSubManifest(string packageLocation)
        {
            int retVal = 0;
            logger.LogVerbose("Hotfix: parseSubManifest received package location {path}", packageLocation);            
            List<HotfixFile> hotfixItems = new List<HotfixFile>();

            XDocument xml = new XDocument();
            try
            {
                xml = XDocument.Load(packageLocation);
                //loop through all dll-file elements
                foreach (XElement dll in xml.Descendants("dll-file"))
                {                    
                    HotfixFile currentItem = new HotfixFile();
                    if(dll.Attribute("is-mismatched-version-ok").Value.Equals("true"))
                    {
                        currentItem.MismatchOK = true;                        
                    }
                    currentItem.FileName = dll.Descendants("dll-package-location").First<XElement>().Value;
                    currentItem.UniqueName = packageArtifactID.ToString() + "_" + dll.Descendants("dll-package-location").First<XElement>().Value;
                    //loop through all installed-location elements in each dll-file. Currently we don't read is-missing-file-ok.
                    foreach (XElement dllLoc in dll.Descendants("installed-location"))
                    {
                        string curLoc = dllLoc.Value;                                                
                        switch (curLoc.Substring(0, curLoc.LastIndexOf('%') + 1))//these will be turned into choices. Add each target server role at most once.
                        {
                            case "%WorkerNetworkPath%":
                                if (currentItem.Roles.Find(i => i.Guid == Constants.Constants.WORKER_ROLE_CHOICE) != null )
                                {
                                    break;
                                }
                                else
                                {
                                    currentItem.Roles.Add(new ChoiceRef() { Guid = Constants.Constants.WORKER_ROLE_CHOICE } );
                                    break;
                                }                                
                            case "%InvariantQueueManager%":
                                if (currentItem.Roles.Find(i => i.Guid == Constants.Constants.WORKERMANAGER_ROLE_CHOICE) != null)
                                {
                                    break;
                                }
                                else
                                {
                                    currentItem.Roles.Add(new ChoiceRef() { Guid = Constants.Constants.WORKERMANAGER_ROLE_CHOICE } );
                                    break;
                                }
                            case "%Relativity%":
                                if (currentItem.Roles.Find(i => i.Guid == Constants.Constants.WEB_ROLE_CHOICE) != null)
                                {
                                    break;
                                }
                                else
                                {
                                    currentItem.Roles.Add(new ChoiceRef() { Guid = Constants.Constants.WEB_ROLE_CHOICE } );
                                    break;
                                }
                            default:
                                if (currentItem.Roles.Find(i => i.Guid == Constants.Constants.UNKNOWN_ROLE_CHOICE) != null)
                                {
                                    break;
                                }
                                else
                                {
                                    currentItem.Roles.Add(new ChoiceRef { Guid = Constants.Constants.UNKNOWN_ROLE_CHOICE } );
                                    break;
                                }
                        }
                        currentItem.Locations += curLoc + "\r\n";                    
                    }
                    //populated class added to list, will be created in loop at the end.
                    hotfixItems.Add(currentItem);
                }
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error Parsing submanifest at {path}",packageLocation);                
                retVal = 1;
                throw; //rethrow to spit up to inline fields.
            }          
          
            //loop through each HotfixFile to build a MassCreateRequest
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var massRequest = new MassCreateRequest();
                //build the massrequest.
                massRequest.ParentObject = new RelativityObjectRef { ArtifactID = packageArtifactID };
                massRequest.ObjectType = new ObjectTypeRef { Name = "Hotfix - Item" };
                var uniqueNameFieldRef = new FieldRef { Guid = Constants.Constants.ITEM_UNIQUE_NAME_FIELD };
                var fileNameFieldRef = new FieldRef { Guid = Constants.Constants.ITEM_FILE_NAME_FIELD };
                var mismatchFieldRef = new FieldRef { Guid = Constants.Constants.ITEM_MISMATCH_VERSION_FIELD };
                var locationFieldRef = new FieldRef { Guid = Constants.Constants.ITEM_LOCATION_FIELD };
                var roleFieldRef = new FieldRef { Guid = Constants.Constants.ITEM_ROLE_FIELD };
                massRequest.Fields = new List<FieldRef> { uniqueNameFieldRef, fileNameFieldRef, mismatchFieldRef, locationFieldRef, roleFieldRef };//this determines the order we need to 
                var requestItemList = new List<List<object>>();
                foreach( HotfixFile curItem in hotfixItems)
                {
                    requestItemList.Add(new List<object> { curItem.UniqueName, curItem.FileName, curItem.MismatchOK, curItem.Locations, curItem.Roles });
                }
                massRequest.ValueLists = requestItemList;

                //execute the massrequest.
                try
                {
                    var massCreateResult = objectManager.CreateAsync(-1, massRequest).Result;
                    if(massCreateResult.Success == false)
                    {
                        logger.LogError("Hotfix: MassCreate partially failed: {message}", massCreateResult.Message);
                        RaiseMessage("MassCreate partially failed: " + massCreateResult.Message, 5);
                        updateInlineFieldsWithResult("Partial Error", massCreateResult.Message);
                    }
                }
                catch (AggregateException ex)
                {//print error for each exception in aggregate, then end.
                    foreach (var exchild in ex.InnerExceptions)
                    {
                        updateInlineFieldsWithResult("Error", ex.ToString());
                        logger.LogError(exchild, "Hotfix: Error Mass Creating Hotfix Items.");                        
                    }
                    retVal = 2;
                }
            }
            return retVal;
        }//end parseSubManifest

        private void exitWithSuccess()
        {
            //remove job row from queue
            SqlParameter packageIDParam = new SqlParameter("PackageArtifactID", SqlDbType.Int);
            SqlParameter queueIDParam = new SqlParameter("QueueID", SqlDbType.Int);
            packageIDParam.Value = packageArtifactID;
            queueIDParam.Value = queueID;
            try
            {
                int rowsaffected = Helper.GetDBContext(-1).ExecuteNonQuerySQLStatement(DELETE_FROM_QUEUE_QUERY, new List<SqlParameter> { packageIDParam, queueIDParam });
                if(rowsaffected == 0)
                {
                    throw new Exception("No row found to delete for package ID" + packageArtifactID.ToString());
                }
            }
            catch(Exception ex)
            {
                logger.LogError(ex, "Hotfix: Error Removing row from Parse queue");
            }

            updateInlineFieldsWithResult("Success");
            RaiseMessage("Completed Parsing for package " + packageArtifactID.ToString(), 10);
        }//end exitWithSuccess

        private void exitWithFailure(string message)
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
                logger.LogError(ex, "Hotfix: Error setting row to Error in Parse queue");
            }

            updateInlineFieldsWithResult("Error",message);            
        }//end exitWithFailure

        //this writes parsing results back to the inline field on the RDO for convenience.
        //handles its own exceptions internally and does not rethrow them (flow can continue if this method fails)
        private void updateInlineFieldsWithResult(string status, string Message = "")
        {            
            using (IObjectManager objectManager = Helper.GetServicesManager().CreateProxy<IObjectManager>(ExecutionIdentity.System))
            {
                var CurrentObject = new RelativityObjectRef { ArtifactID = packageArtifactID };
                var parseStatusFVP = new FieldRefValuePair
                {
                    Field = new FieldRef() { Guid = Constants.Constants.PARSE_STATUS_FIELD },
                    Value = status
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
                    var objManResult = objectManager.UpdateAsync(-1, updateRequest).Result;
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



        //class to hold data for each hotfix item before being written to the database.
        public class HotfixFile
        {
            public HotfixFile()
            {
                MismatchOK = false;
                UniqueName = "PREINIT";
                FileName = "PREINIT";
                Roles = new List<ChoiceRef>();
                Locations = "";                
            }
            public string Locations { get; set; }
            public bool MismatchOK { get; set; }
            public string FileName { get; set; }
            public string UniqueName { get; set; }
            public List<ChoiceRef> Roles { get; set; }
        }//end class HotfixFile

        /// <summary>
        /// Returns the name of agent
        /// </summary>
        public override string Name
        {
            get
            {
                return "Hotfix Parse Agent";
            }
        }
    }
}