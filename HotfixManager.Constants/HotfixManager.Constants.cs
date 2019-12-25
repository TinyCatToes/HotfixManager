using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HotfixManager.Constants
{
    public static class Constants
    {
        public static readonly Guid HOTFIX_OBJECT_TYPE = new Guid("AC8033C5-D77F-4A29-B53A-9C34CA1F8C17");
        public static readonly Guid UPLOAD_FILE_FIELD = new Guid("5C5A9B83-08DA-4964-B4B9-D47645A48A25");
        public static readonly Guid PARSE_STATUS_FIELD = new Guid("EBB4E027-3423-4EC4-AB6F-A6BB6CCD3BCF");
        public static readonly Guid PARSE_ERROR_FIELD = new Guid("CB317ED1-4BC9-4F60-BB35-6D3ABECB0B95");
        public static readonly Guid NAME_FIELD = new Guid("A4B2F993-7C42-484F-BD48-BF04FD33C3AE");
        public static readonly Guid VERSION_FIELD = new Guid("B2C0C7E3-5E9E-4EC7-8884-BC02AF17B485");
        public static readonly Guid DISK_LOCATION_FIELD = new Guid("EE11603E-D308-48AC-ABCD-4866927B1F78");
        public static readonly Guid LAST_RUN_STATUS_FIELD = new Guid("85168203-D740-4CC8-A0BA-0225498E5472");
        public static readonly Guid LAST_RUN_TIME_FIELD = new Guid("C44680A2-F302-4152-A9E8-435FFB1C9385");
        public static readonly Guid LAST_RUN_QUEUED_CHOICE = new Guid("6B853368-EA27-4595-B39E-57C0E0831A33");
        public static readonly Guid LAST_RUN_INPROG_CHOICE = new Guid("9D7E3A4D-296A-4C39-8617-41526D49BA11");
        public static readonly Guid LAST_RUN_ERROR_CHOICE = new Guid("93E7E863-AC99-4DA5-8837-4CBB365E74EC");
        public static readonly Guid LAST_RUN_COMPLETE_CHOICE = new Guid("A407D3A2-3911-4CF2-BF5A-D12B0FB3EF8B");
        public static readonly Guid LAST_RUN_CANCELLED_CHOICE = new Guid("B2E39212-9F8A-41F4-AE24-C66A4F511035");
        public static readonly Guid ITEM_UNIQUE_NAME_FIELD = new Guid("1B321FD5-DD93-45FF-A8AA-90C1F7676F87");
        public static readonly Guid ITEM_FILE_NAME_FIELD = new Guid("5338FD8F-79A8-4719-8ECF-937D079EE373");
        public static readonly Guid ITEM_ROLE_FIELD = new Guid("80A61937-7978-4C2C-A2E1-0EC6476A2789");
        public static readonly Guid ITEM_LOCATION_FIELD = new Guid("1A4F99B9-4282-4A57-8A62-7C68F5F44110");
        public static readonly Guid ITEM_MISMATCH_VERSION_FIELD = new Guid("B7919304-06E0-4FD3-890A-B199838765F2");
        public static readonly Guid WORKER_ROLE_CHOICE = new Guid("ACD507CE-EDCC-486D-9ABE-99F3D0A1CCD0");
        public static readonly Guid WORKERMANAGER_ROLE_CHOICE = new Guid("E6997E8D-9DE8-4A01-920B-C392DDDBDE5A");
        public static readonly Guid WEB_ROLE_CHOICE = new Guid("FC09039D-CB0E-4E91-86C8-F3D393BE2A60");
        public static readonly Guid UNKNOWN_ROLE_CHOICE = new Guid("D49BE912-7A84-4C60-9DF8-A6824AF7448B");
        public static readonly Guid LAST_RUN_ERROR_FIELD = new Guid("BF7AFD20-49A0-48F4-A00E-585963A91CCB");
        public static readonly Guid LOG_STATUS_INPROG_CHOICE = new Guid("4694B4B7-869C-428D-A15E-159E2D71AA6C");
        public static readonly Guid LOG_STATUS_COMPLETE_CHOICE = new Guid("72575DEC-C0EC-4F83-8C48-0DF4AB3676A0");
        public static readonly Guid LOG_STATUS_ERROR_CHOICE = new Guid("80C93967-FB57-4966-87BB-2A0B9FE8D203");
    }
}