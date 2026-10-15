using System;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace BIMExporter.Export
{
    public static class StableId
    {
        public static string ForElement(Document document, Element element)
        {
            var docKey = document.ProjectInformation != null
                ? document.ProjectInformation.UniqueId
                : document.PathName;
            var raw = string.Format("{0}|{1}", docKey, element.UniqueId);

            return Hash(raw);
        }

        public static string ForLinkedElement(Document host, string linkPath, Document document, Element element)
        {
            return Hash(host.ProjectInformation.UniqueId + "|" + linkPath + "|" + ForElement(document, element));
        }

        private static string Hash(string raw)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return "bim-" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
