// using System;
// using System.Threading;
// using System.Threading.Tasks;
// using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
// using Microsoft.Extensions.Logging;
// using Moq;
// using Xunit;
//
// namespace Bakabase.Tests
// {
//     public class HardwareAccelerationTests
//     {
//         private readonly Mock<ILogger<HardwareAccelerationService>> _loggerMock;
//         private readonly Mock<FfMpegService> _ffMpegServiceMock;
//
//         public HardwareAccelerationTests()
//         {
//             _loggerMock = new Mock<ILogger<HardwareAccelerationService>>();
//             _ffMpegServiceMock = new Mock<FfMpegService>();
//         }
//
//         [Fact]
//         public async Task Test_HardwareAccelerationInfo_DefaultValues()
//         {
//             // Arrange
//             var service = new HardwareAccelerationService(_loggerMock.Object, _ffMpegServiceMock.Object);
//
//             // Act
//             var info = new HardwareAccelerationInfo();
//
//             // Assert
//             Assert.False(info.IsDetected);
//             Assert.Equal("libx264", info.PreferredCodec);
//             Assert.Empty(info.AvailableCodecs);
//         }
//
//         [Fact]
//         public async Task Test_HardwareAccelerationService_CacheBehavior()
//         {
//             // Arrange
//             _ffMpegServiceMock.Setup(x => x.FfMpegExecutable).Returns("ffmpeg");
//             var service = new HardwareAccelerationService(_loggerMock.Object, _ffMpegServiceMock.Object);
//
//             // Act & Assert
//             // First call should detect hardware acceleration
//             var info1 = await service.GetHardwareAccelerationInfoAsync();
//             Assert.True(info1.IsDetected);
//
//             // Second call should return cached result
//             var info2 = await service.GetHardwareAccelerationInfoAsync();
//             Assert.True(info2.IsDetected);
//             Assert.Equal(info1.PreferredCodec, info2.PreferredCodec);
//
//             // Clear cache
//             service.ClearCache();
//
//             // Third call should detect again
//             var info3 = await service.GetHardwareAccelerationInfoAsync();
//             Assert.True(info3.IsDetected);
//         }
//
//         [Fact]
//         public async Task Test_HardwareAccelerationService_ThreadSafety()
//         {
//             // Arrange
//             _ffMpegServiceMock.Setup(x => x.FfMpegExecutable).Returns("ffmpeg");
//             var service = new HardwareAccelerationService(_loggerMock.Object, _ffMpegServiceMock.Object);
//
//             // Act
//             var tasks = new Task<HardwareAccelerationInfo>[10];
//             for (int i = 0; i < 10; i++)
//             {
//                 tasks[i] = service.GetHardwareAccelerationInfoAsync();
//             }
//
//             var results = await Task.WhenAll(tasks);
//
//             // Assert
//             Assert.All(results, info => Assert.True(info.IsDetected));
//             Assert.All(results, info => Assert.NotNull(info.PreferredCodec));
//         }
//     }
// } 