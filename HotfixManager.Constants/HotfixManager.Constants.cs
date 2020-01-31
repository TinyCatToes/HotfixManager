using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HotfixManager.Constants
{
    public static class Constants
    {
        public static readonly Guid HOTFIX_OBJECT_TYPE = new Guid("5120F945-238B-48FF-9E64-782DEFF28132");
        public static readonly Guid UPLOAD_FILE_FIELD = new Guid("7D14EA66-A907-43FD-BC5B-F014A608EFB3");
        public static readonly Guid PARSE_STATUS_FIELD = new Guid("E24D1BA2-A5E9-4119-8A5E-4AE40C53EA4F");
        public static readonly Guid PARSE_ERROR_FIELD = new Guid("663197DB-FCBE-4602-8C81-30FCF3E565EE");
        public static readonly Guid NAME_FIELD = new Guid("1CFC1E67-217B-4013-8740-92B7ADE7424E");
        public static readonly Guid VERSION_FIELD = new Guid("1663A7D2-99DE-4AF5-AB34-8697626705DD");
        public static readonly Guid DISK_LOCATION_FIELD = new Guid("F756C5D4-2730-4CFD-AD31-1698A177A0C6");
        public static readonly Guid LAST_RUN_STATUS_FIELD = new Guid("301E00C4-5433-481C-BAD0-21A4218E953C");
        public static readonly Guid LAST_RUN_TIME_FIELD = new Guid("D5EDB964-EB8B-4E26-B887-BF82A8C9610E");
        public static readonly Guid LAST_RUN_QUEUED_CHOICE = new Guid("67867F91-31F0-48C7-874E-00FF32BAF86C");
        public static readonly Guid LAST_RUN_INPROG_CHOICE = new Guid("35662C4F-29D2-4393-9372-7BF4B697CF12");
        public static readonly Guid LAST_RUN_ERROR_CHOICE = new Guid("B03AB88F-7E55-40FB-A442-993393706055");
        public static readonly Guid LAST_RUN_COMPLETE_CHOICE = new Guid("F7E59B4F-BCA6-458A-8418-8096AA32991A");
        public static readonly Guid LAST_RUN_CANCELLED_CHOICE = new Guid("5D2F597C-4C04-4CF2-985B-FEBBF6C8B73B");
        public static readonly Guid ITEM_UNIQUE_NAME_FIELD = new Guid("C20168CB-9786-47DF-9A59-FE0EF38E6779");
        public static readonly Guid ITEM_FILE_NAME_FIELD = new Guid("84B2E0B9-00B8-4F03-8C2B-66BE1998DC15");
        public static readonly Guid ITEM_ROLE_FIELD = new Guid("607D9AC8-95DF-47AC-9AFF-1B2E1B931EDB");
        public static readonly Guid ITEM_LOCATION_FIELD = new Guid("DD3847CC-78BA-4694-9716-F25D5532E660");
        public static readonly Guid ITEM_MISMATCH_VERSION_FIELD = new Guid("F9A98E32-9DC4-45DE-A41E-2BCF3C96E65D");
        public static readonly Guid WORKER_ROLE_CHOICE = new Guid("31B28926-F362-4D9D-B84C-97E51140AA8F");
        public static readonly Guid WORKERMANAGER_ROLE_CHOICE = new Guid("4959D2F8-20FA-4B65-AD70-FCD837BBC77A");
        public static readonly Guid WEB_ROLE_CHOICE = new Guid("6ABF95DC-0AC7-48B5-B22F-354E68F55259");
        public static readonly Guid UNKNOWN_ROLE_CHOICE = new Guid("755773E7-5358-4698-B2FE-B115D7D9D414");
        public static readonly Guid LAST_RUN_ERROR_FIELD = new Guid("75F0D636-0EA6-447C-8BCE-EDC89CBD67BF");
        public static readonly Guid LOG_STATUS_FIELD = new Guid("D44455E7-12F4-4456-955A-24AF551483E6");
        public static readonly Guid LOG_STATUS_INPROG_CHOICE = new Guid("C37FF4FA-6B4F-4D24-B340-F8960B2E0871");
        public static readonly Guid LOG_STATUS_COMPLETE_CHOICE = new Guid("21C52E1B-196D-45CA-A88C-C9D4F53A0FCB");
        public static readonly Guid LOG_STATUS_ERROR_CHOICE = new Guid("8B514320-5123-4108-B861-834391A20606");
    }
}