using Newtonsoft.Json;
using System;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux.Models
{
    public class GithubAuthor
    {
        [JsonProperty("login")] public string Login { get; set; }

        [JsonProperty("id")] public long Id { get; set; }

        [JsonProperty("node_id")] public string NodeId { get; set; }

        [JsonProperty("avatar_url")] public Uri AvatarUrl { get; set; }

        [JsonProperty("gravatar_id")] public string GravatarId { get; set; }

        [JsonProperty("url")] public string Url { get; set; }

        [JsonProperty("html_url")] public Uri HtmlUrl { get; set; }

        [JsonProperty("followers_url")] public string FollowersUrl { get; set; }

        [JsonProperty("following_url")] public string FollowingUrl { get; set; }

        [JsonProperty("gists_url")] public string GistsUrl { get; set; }

        [JsonProperty("starred_url")] public string StarredUrl { get; set; }

        [JsonProperty("subscriptions_url")] public string SubscriptionsUrl { get; set; }

        [JsonProperty("organizations_url")] public string OrganizationsUrl { get; set; }

        [JsonProperty("repos_url")] public string ReposUrl { get; set; }

        [JsonProperty("events_url")] public string EventsUrl { get; set; }

        [JsonProperty("received_events_url")] public string ReceivedEventsUrl { get; set; }

        [JsonProperty("type")] public string Type { get; set; }

        [JsonProperty("site_admin")] public bool SiteAdmin { get; set; }
    }
}