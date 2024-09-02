namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models
{
    public class DataWrapper<T>
    {
        public int Code { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
    }
}