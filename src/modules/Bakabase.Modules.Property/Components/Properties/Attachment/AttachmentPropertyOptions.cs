namespace Bakabase.Modules.Property.Components.Properties.Attachment
{
    public enum AttachmentLayout
    {
        Tile = 0,
        Carousel = 1,
    }

    public class AttachmentPropertyOptions
    {
        public AttachmentLayout Layout { get; set; } = AttachmentLayout.Tile;
    }
}
