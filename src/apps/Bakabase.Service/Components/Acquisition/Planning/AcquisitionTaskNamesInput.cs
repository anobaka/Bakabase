using System.Collections.Generic;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionTaskNamesInput(IReadOnlyList<int> TaskIds);
