namespace Bakabase.InsideWorld.Models.Constants
{
    public enum SpecialTextType
    {
        /// <summary>
        ///     Value1为有效值
        /// </summary>
        Useless = 1,

        /// <summary>
        ///     Value1 for matching, Value2 for language enum.
        /// </summary>
        Language = 2,

        Wrapper = 3,

        /// <summary>
        ///     Make name standard while comparing. Value1 to Value2.
        /// </summary>
        Standardization = 4,

        /// <summary>
        /// 
        /// </summary>
        Volume = 6,
        Trim = 7,
        /// <summary>
        /// Make sure Value1 follows naming convention of <see cref="DateTime.TryParseExact"/>
        /// </summary>
        DateTime = 8,
    }
}