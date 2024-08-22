using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg
{
    public class FfMpegDiscoverer : ExecutableDiscoverer
    {
        public FfMpegDiscoverer(ILoggerFactory loggerFactory) : base(loggerFactory)
        {
        }

        protected override HashSet<string> RequiredRelativeFileNamesWithoutExtensions { get; } =
            new HashSet<string> {"ffmpeg", "ffprobe"};

        protected override string RelativeFileNameWithoutExtensionForAcquiringVersion => "ffmpeg";
        protected override string ArgumentsForAcquiringVersion => "-version";

        protected override string ParseVersion(string output)
        {
            // ffmpeg version 2021-01-31-git-6c92557756-full_build-www.gyan.dev Copyright (c) 2000-2021 the FFmpeg developers
            // built with gcc 10.2.0 (Rev6, Built by MSYS2 project)
            // configuration: --enable-gpl --enable-version3 --enable-static --disable-w32threads --disable-autodetect --enable-fontconfig --enable-iconv --enable-gnutls --enable-libxml2 --enable-gmp --enable-lzma --enable-libsnappy --enable-zlib --enable-libsrt --enable-libssh --enable-libzmq --enable-avisynth --enable-libbluray --enable-libcaca --enable-sdl2 --enable-libdav1d --enable-libzvbi --enable-librav1e --enable-libsvtav1 --enable-libwebp --enable-libx264 --enable-libx265 --enable-libxvid --enable-libaom --enable-libopenjpeg --enable-libvpx --enable-libass --enable-frei0r --enable-libfreetype --enable-libfribidi --enable-libvidstab --enable-libvmaf --enable-libzimg --enable-amf --enable-cuda-llvm --enable-cuvid --enable-ffnvcodec --enable-nvdec --enable-nvenc --enable-d3d11va --enable-dxva2 --enable-libmfx --enable-libglslang --enable-vulkan --enable-opencl --enable-libcdio --enable-libgme --enable-libmodplug --enable-libopenmpt --enable-libopencore-amrwb --enable-libmp3lame --enable-libshine --enable-libtheora --enable-libtwolame --enable-libvo-amrwbenc --enable-libilbc --enable-libgsm --enable-libopencore-amrnb --enable-libopus --enable-libspeex --enable-libvorbis --enable-ladspa --enable-libbs2b --enable-libflite --enable-libmysofa --enable-librubberband --enable-libsoxr --enable-chromaprint
            // libavutil      56. 64.100 / 56. 64.100
            // libavcodec     58.119.100 / 58.119.100
            // libavformat    58. 65.101 / 58. 65.101
            // libavdevice    58. 11.103 / 58. 11.103
            // libavfilter     7.100.100 /  7.100.100
            // libswscale      5.  8.100 /  5.  8.100
            // libswresample   3.  8.100 /  3.  8.100
            // libpostproc    55.  8.100 / 55.  8.100

            var firstLine = output.Split(Environment.NewLine)[0];
            var segments = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var versionIndex = segments.IndexOf("version");
            if (versionIndex == -1)
            {
                throw new Exception("'version' is not found in output");
            }

            if (segments.Count < versionIndex + 2)
            {
                throw new Exception("There is no version info after 'version'");
            }

            return segments[versionIndex + 1];
        }
    }
}