namespace Bakabase.InsideWorld.Business.Components.ThirdParty.Bilibili.Models
{
    public class DataWrapper<T>
    {
        public int Code { get; set; }
        public T Data { get; set; }
        public string Message { get; set; }
    }
}