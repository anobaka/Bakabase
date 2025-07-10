using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.FileNameModifier.Abstractions
{
    /// <summary>
    /// 文件名修改器接口
    /// </summary>
    public interface IFileNameModifier
    {
        /// <summary>
        /// 批量修改文件名
        /// </summary>
        /// <param name="fileNames">原始文件名列表</param>
        /// <param name="operations">修改操作列表</param>
        /// <returns>修改后的文件名列表</returns>
        List<string> ModifyFileNames(List<string> fileNames, List<Models.FileNameModifierOperation> operations);
        
        /// <summary>
        /// 预览修改结果
        /// </summary>
        /// <param name="fileName">原始文件名</param>
        /// <param name="operations">修改操作列表</param>
        /// <returns>修改后的文件名</returns>
        string PreviewModification(string fileName, List<Models.FileNameModifierOperation> operations);
        
        /// <summary>
        /// 验证操作配置是否有效
        /// </summary>
        /// <param name="operation">操作配置</param>
        /// <returns>验证结果</returns>
        bool ValidateOperation(Models.FileNameModifierOperation operation);
    }
} 