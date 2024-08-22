using Bakabase.InsideWorld.Business.Components.FileMover.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileMover
{
    public interface IFileMover
    {
        void TryStartMovingFiles();
        ConcurrentDictionary<string, FileMovingProgress> Progresses { get; }
    }
}
