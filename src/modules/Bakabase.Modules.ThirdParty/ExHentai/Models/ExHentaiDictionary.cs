namespace Bakabase.Modules.ThirdParty.ExHentai.Models
{
    public class ExHentaiDictionary
    {
        public HeadModel Head { get; set; }
        public int Version { get; set; }
        public DataModel[] Data { get; set; }

        public class HeadModel
        {
            public string Sha { get; set; }
        }

        public class DataModel
        {
            public string Namespace { get; set; }
            public FrontMattersModel FrontMatters { get; set; }
            public int Count { get; set; }
            public Dictionary<string, Word> Data { get; set; }

            public class FrontMattersModel
            {
                public string Name { get; set; }
                public string Description { get; set; }
                public string Key { get; set; }
                public string[] Rules { get; set; }
            }

            public class Word
            {
                public string Name { get; set; }
                public string Intro { get; set; }
                public string Links { get; set; }
            }
        }
    }
}