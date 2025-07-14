using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg.Models;
using Bootstrap.Components.Tasks;
using CliWrap;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg
{
    public class HardwareAccelerationService
    {
        private readonly ILogger<HardwareAccelerationService> _logger;
        private readonly FfMpegService _ffMpegService;
        private readonly ConcurrentDictionary<string, HardwareAccelerationInfo> _cache = new();
        private readonly SemaphoreSlim _detectionSemaphore = new(1, 1);

        public HardwareAccelerationService(ILogger<HardwareAccelerationService> logger, FfMpegService ffMpegService)
        {
            _logger = logger;
            _ffMpegService = ffMpegService;
        }

        public async Task<HardwareAccelerationInfo> GetHardwareAccelerationInfoAsync(CancellationToken ct = default)
        {
            const string cacheKey = "hardware_acceleration_info";
            
            if (_cache.TryGetValue(cacheKey, out var cachedInfo))
            {
                return cachedInfo;
            }

            await _detectionSemaphore.WaitAsync(ct);
            try
            {
                // Double-check after acquiring semaphore
                if (_cache.TryGetValue(cacheKey, out cachedInfo))
                {
                    return cachedInfo;
                }

                var info = await DetectHardwareAccelerationAsync(ct);
                _cache.TryAdd(cacheKey, info);
                return info;
            }
            finally
            {
                _detectionSemaphore.Release();
            }
        }

        private async Task<HardwareAccelerationInfo> DetectHardwareAccelerationAsync(CancellationToken ct)
        {
            var info = new HardwareAccelerationInfo();
            
            try
            {
                // Check NVIDIA NVENC
                if (await CheckCodecAvailabilityAsync("h264_nvenc", ct))
                {
                    info.AvailableCodecs.Add("h264_nvenc");
                    info.PreferredCodec = "h264_nvenc";
                    _logger.LogInformation("NVIDIA NVENC hardware acceleration detected");
                }

                // Check Intel QSV
                if (await CheckCodecAvailabilityAsync("h264_qsv", ct))
                {
                    info.AvailableCodecs.Add("h264_qsv");
                    if (string.IsNullOrEmpty(info.PreferredCodec))
                    {
                        info.PreferredCodec = "h264_qsv";
                    }
                    _logger.LogInformation("Intel Quick Sync Video hardware acceleration detected");
                }

                // Check AMD AMF
                if (await CheckCodecAvailabilityAsync("h264_amf", ct))
                {
                    info.AvailableCodecs.Add("h264_amf");
                    if (string.IsNullOrEmpty(info.PreferredCodec))
                    {
                        info.PreferredCodec = "h264_amf";
                    }
                    _logger.LogInformation("AMD AMF hardware acceleration detected");
                }

                // Check Apple VideoToolbox (for macOS)
                if (await CheckCodecAvailabilityAsync("h264_videotoolbox", ct))
                {
                    info.AvailableCodecs.Add("h264_videotoolbox");
                    if (string.IsNullOrEmpty(info.PreferredCodec))
                    {
                        info.PreferredCodec = "h264_videotoolbox";
                    }
                    _logger.LogInformation("Apple VideoToolbox hardware acceleration detected");
                }

                // If no hardware acceleration is available, fall back to software encoding
                if (string.IsNullOrEmpty(info.PreferredCodec))
                {
                    info.PreferredCodec = "libx264";
                    _logger.LogInformation("No hardware acceleration detected, using software encoding (libx264)");
                }

                info.IsDetected = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting hardware acceleration, falling back to software encoding");
                info.PreferredCodec = "libx264";
                info.IsDetected = true;
            }

            return info;
        }

        private async Task<bool> CheckCodecAvailabilityAsync(string codecName, CancellationToken ct)
        {
            try
            {
                var output = new StringBuilder();
                var error = new StringBuilder();

                // Create a timeout cancellation token
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                var cmd = Cli.Wrap(_ffMpegService.FfMpegExecutable)
                    .WithArguments(new[]
                    {
                        "-hide_banner",
                        "-f", "lavfi",
                        "-i", "testsrc=duration=1:size=320x240:rate=1",
                        "-c:v", codecName,
                        "-f", "null",
                        "-"
                    })
                    .WithValidation(CommandResultValidation.None)
                    .WithStandardOutputPipe(PipeTarget.ToStringBuilder(output))
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(error));

                var result = await cmd.ExecuteAsync(combinedCts.Token);
                
                // If exit code is 0, the codec is available
                return result.ExitCode == 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // User cancellation
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout or other cancellation
                _logger.LogDebug("Codec {CodecName} check timed out", codecName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Codec {CodecName} is not available", codecName);
                return false;
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
        }
    }

    public class HardwareAccelerationInfo
    {
        public bool IsDetected { get; set; }
        public string PreferredCodec { get; set; } = "libx264";
        public List<string> AvailableCodecs { get; set; } = new();
    }
} 