namespace Bakabase.Modules.ThirdParty.ThirdParties.Pixiv.Models
{
    public class PixivBaseResponse
    {
        public bool Error { get; set; }
        public string Message { get; set; }
    }

    public class PixivBaseResponse<T> : PixivBaseResponse
    {
        public T Body { get; set; }
    }
}