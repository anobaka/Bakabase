using Bakabase.Modules.Acquisition.Abstractions.Models.Db;

namespace Bakabase.Modules.Acquisition.Components;

public static class AcquisitionTaskGuard
{
    public static void EnsureNotDeleted(AcquisitionTaskDbModel task)
    {
        if (task.DeletedAt != null)
            throw new InvalidOperationException("ACQUISITION_TASK_DELETED: This task was deleted. Its files and execution history are retained.");
    }
}
