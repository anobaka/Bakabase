using CliWrap;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components
{
    public class CustomCliWrapPipeTarget : PipeTarget
    {
        private readonly Func<string, Task> _onRead;
        private readonly Encoding _encoding;

        public CustomCliWrapPipeTarget(Func<string, Task> onRead, Encoding encoding)
        {
            _onRead = onRead;
            _encoding = encoding;
        }

        public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
        {
            var buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _onRead(_encoding.GetString(buffer.Take(bytesRead).ToArray()));
            }
        }
    }
}