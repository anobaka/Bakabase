namespace Bakabase.InsideWorld.Business.Components.FileNameModifier.Models
{
    /// <summary>
    /// 文件名目标区域枚举
    /// </summary>
    public enum FileNameModifierFileNameTarget
    {
        /// <summary>
        /// 完整文件名（包含扩展名）
        /// </summary>
        FileName = 1,
        /// <summary>
        /// 文件名（不含扩展名）
        /// </summary>
        FileNameWithoutExtension,
        /// <summary>
        /// 扩展名（包含分隔符）
        /// </summary>
        Extension,
        /// <summary>
        /// 扩展名（不包含分隔符）
        /// </summary>
        ExtensionWithoutDot
    }
} 