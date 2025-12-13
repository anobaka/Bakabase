namespace Bakabase.InsideWorld.Business.Components.FileNameModifier.Models
{
    /// <summary>
    /// 修改操作配置
    /// </summary>
    public class FileNameModifierOperation
    {
        /// <summary>
        /// 目标区域
        /// </summary>
        public FileNameModifierFileNameTarget Target { get; set; }
        /// <summary>
        /// 操作类型
        /// </summary>
        public FileNameModifierOperationType Operation { get; set; }
        /// <summary>
        /// 位置
        /// </summary>
        public FileNameModifierPosition Position { get; set; }
        /// <summary>
        /// 位置索引（用于AtPosition）
        /// </summary>
        public int PositionIndex { get; set; }
        /// <summary>
        /// 目标字符（用于AfterText/BeforeText）
        /// </summary>
        public string? TargetText { get; set; }
        /// <summary>
        /// 插入/替换的文本
        /// </summary>
        public string? Text { get; set; }
        /// <summary>
        /// 删除的字符数量
        /// </summary>
        public int DeleteCount { get; set; }
        /// <summary>
        /// 删除的起始位置
        /// </summary>
        public int DeleteStartPosition { get; set; }
        /// <summary>
        /// 大小写类型
        /// </summary>
        public FileNameModifierCaseType CaseType { get; set; }
        /// <summary>
        /// 日期时间格式
        /// </summary>
        public string? DateTimeFormat { get; set; }
        /// <summary>
        /// 字母序号起始字符
        /// </summary>
        public char AlphabetStartChar { get; set; }
        /// <summary>
        /// 字母序号数量
        /// </summary>
        public int AlphabetCount { get; set; }
        /// <summary>
        /// 是否替换整个内容
        /// </summary>
        public bool ReplaceEntire { get; set; }
        /// <summary>
        /// 是否使用正则表达式匹配（用于Replace操作）
        /// </summary>
        public bool Regex { get; set; }
    }
} 