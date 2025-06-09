using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Ai;

public record Post
{
    public string? Title { get; set; }
    public List<Resource>? Resources { get; set; }
    public record Resource
    {
        public string? Link { get; set; }
        public string? Code { get; set; }
        public string? Password { get; set; }
    }

    public Post Optimize()
    {
        if (string.IsNullOrEmpty(Title))
        {
            Title = null;
        }

        Resources = Resources?.Where(r => !string.IsNullOrEmpty(r.Link)).ToList();
        if (Resources != null)
        {
            foreach (var r in Resources)
            {
                if (string.IsNullOrEmpty(r.Code))
                {
                    r.Code = null;
                }

                if (string.IsNullOrEmpty(r.Password))
                {
                    r.Password = null;
                }
            }

            if (Resources.Count == 0)
            {
                Resources = null;
            }
        }

        return this;
    }
}