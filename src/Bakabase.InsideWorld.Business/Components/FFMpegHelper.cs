using Bakabase.InsideWorld.Models.Configs;
using CliWrap;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Resources;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Business.Components
{
    public class FFMpegHelper(IBOptions<ThirdPartyOptions> thirdPartyOptions, InsideWorldLocalizer localizer)
    {
        public async Task<double> GetDuration(string path, CancellationToken ct)
        {
            // https://trac.ffmpeg.org/ticket/8890
            // Console.OutputEncoding = Encoding.UTF8;
            // ffprobe.exe simply writes utf8 data to stdout, and the Xabe.FFmpeg has no way to specify encoding.
            // var info = await FFmpeg.GetMediaInfo(firstVideoFile.FullName, ct);
            var output = new StringBuilder();
            var error = new StringBuilder();
            var cmd = Cli.Wrap(FfProbeExecutable)
                .WithArguments(new[]
                {
                    "-v", "quiet",
                    "-print_format", "json",
                    "-show_entries", "format=duration",
                    path
                }, true)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStringBuilder(output))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(error));

            // var x2 = await FFmpeg.GetMediaInfo(firstVideoFile.FullName, ct);
            var rsp = await cmd.ExecuteAsync(ct);
            if (rsp.ExitCode != 0)
            {
                throw new Exception(error.ToString());
            }

            var jObject = JObject.Parse(output.ToString());
            var seconds = jObject["format"]!["duration"]!.Value<double>();
            return seconds;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="videoFilePath"></param>
        /// <param name="time"></param>
        /// <param name="ct"></param>
        /// <returns>Jpeg format</returns>
        /// <exception cref="Exception"></exception>
        public async Task<MemoryStream> CaptureFrame(string videoFilePath, TimeSpan time, CancellationToken ct)
        {
            // var c = (await FFmpeg.Conversions.FromSnippet.Snapshot(firstVideoFile.FullName,
            //     tmpFile,
            //     screenshotTime));
            // var r = await c.Start(ct);
            // ffmpeg -i input.mp4 -ss 00:00:05 -vframes 1 frame_out.jpg1.
            var output = new StringBuilder();
            var error = new StringBuilder();
            var image = new MemoryStream();
            var timeString = $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
            var cmd = Cli.Wrap($"{thirdPartyOptions.Value.FFmpeg!.BinDirectory}/ffmpeg")
                .WithArguments(new[]
                {
                    "-ss", timeString,
                    "-r", "1:1",
                    "-i", videoFilePath,
                    "-c:v", "mjpeg",
                    "-f", "image2pipe",
                    "-vframes:v", "1",
                    "-preset", "ultrafast",
                    "-"
                }, true)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToStream(image))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(error));
            var rsp = await cmd.ExecuteAsync(ct);
            if (rsp.ExitCode != 0)
            {
                throw new Exception(error.ToString());
            }

            image.Seek(0, SeekOrigin.Begin);
            return image;
        }
    }
}