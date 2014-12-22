using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLib
{
    public static class EventProjectorUtils
    {
        public static async Task<int> Save(
            IDocumentFolder folder, string documentName, int expectedVersion, string newContents,
            IList<DocumentIndexing> indexes)
        {
            var saved = await folder.SaveDocument(
                documentName, newContents, DocumentStoreVersion.At(expectedVersion), indexes);
            if (saved)
                return expectedVersion + 1;
            else
                throw new ProjectorMessages.ConcurrencyException();
        }
    }
}