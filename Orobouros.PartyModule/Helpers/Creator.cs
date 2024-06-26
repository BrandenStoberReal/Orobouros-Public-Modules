﻿using System.Drawing;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Orobouros.Bases;
using Orobouros.Managers;

namespace Orobouros.PartyModule;

public class Creator : HttpAPIAsset
{
    /// <summary>
    ///     List of supported services
    /// </summary>
    private readonly List<string> _servicesList = new()
    {
        "fanbox",
        "patreon",
        "fantia",
        "subscribestar",
        "gumroad",
        "boosty",
        "onlyfans",
        "fansly",
        "candfans"
    };

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="url">Creator's party site URL</param>
    public Creator(string url)
    {
        URL = url; // Simple variable setting

        // HTTP init
        var response = HttpManager.GET(url);

        if (!response.Successful)
        {
            Successful = false;
            Errored = false;
            ResponseCode = response.ResponseCode;
            return;
        }

        if (response.Errored)
        {
            Errored = true;
            Successful = false;
            ResponseCode = response.ResponseCode;
            return;
        }

        if (response.Successful && !response.Errored)
        {
            Successful = true;
            Errored = false;
            ResponseCode = response.ResponseCode;
        }

        // Load HTML
        var responseDocument = new HtmlDocument();
        responseDocument.LoadHtml(response.Content.ReadAsStringAsync().Result);
        LandingPage = responseDocument;

        // Populate name variable
        var creatorNameNode = responseDocument.DocumentNode.SelectNodes("//span[@itemprop]").FirstOrDefault();
        Name = creatorNameNode?.InnerText;

        // Identify service
        if (_servicesList.Any(url.Contains))
            Service = _servicesList.Find(url.Contains);
        else
            // Unsupported service
            Service = null;

        // Fetch domain URL
        var reg = new Regex("https://[A-Za-z0-9]+\\.su");
        var regMatch = reg.Match(url);
        if (regMatch.Success)
            PartyDomain = regMatch.Value;
        else
            PartyDomain = null;

        // Populate total posts
        TotalPosts = GetTotalPosts();
    }

    /// <summary>
    ///     Human-readable name of the creator
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     The creator's party site URL
    /// </summary>
    public string URL { get; private set; }

    /// <summary>
    ///     The archival service used by the creator
    /// </summary>
    public string? Service { get; private set; }

    /// <summary>
    ///     Whether the creator's instance is from Coomer.su or Kemono.su
    /// </summary>
    public string? PartyDomain { get; private set; }

    /// <summary>
    ///     Creator's landing page source code. This is the HTML displayed upon first clicking a creator's banner.
    /// </summary>
    public HtmlDocument LandingPage { get; }

    /// <summary>
    ///     Total posts the creator has on their respective page.
    /// </summary>
    public int? TotalPosts { get; }

    /// <summary>
    ///     Cache variable for GetProfilePicture()
    /// </summary>
    private Image? ProfilePicture { get; set; }

    /// <summary>
    ///     Cache variable for GetProfileBanner()
    /// </summary>
    private Image? ProfileBanner { get; } = null;


    #region Methods

    /// <summary>
    ///     Fetches the total number of posts a creator has on their service
    /// </summary>
    /// <returns></returns>
    private int? GetTotalPosts()
    {
        // Posts integer is ambiguous and fetches all posts
        var paginatorContainer = HtmlManager.SelectNodesByClass(LandingPage, "paginator", "div").FirstOrDefault();
        if (paginatorContainer != null)
        {
            var totalPostsNode = HtmlManager.FetchChildNodes(paginatorContainer)
                .FirstOrDefault(x => x.Name == "small");
            if (totalPostsNode != null) return int.Parse(totalPostsNode.InnerText.Replace("Showing 1 - 50 of ", ""));
        }

        return null;
    }

    /// <summary>
    ///     Fetches a creator's profile picture
    /// </summary>
    /// <returns></returns>
    public Image? GetProfilePicture()
    {
        if (ProfilePicture == null)
        {
            // Fetch the actual image URL
            var imageNode = LandingPage.DocumentNode.Descendants().FirstOrDefault(x =>
                x.HasClass("fancy-image__image") && x.Name == "img" && x.Attributes["src"].Value.Contains("icons"));

            // HTTP Weird Stuff
            var profilePicData = HttpManager.GET("https:" + imageNode.Attributes["src"].Value);
            if (!profilePicData.Errored)
            {
                using var ms = profilePicData.Content.ReadAsStreamAsync().Result;
                ProfilePicture = Image.FromStream(ms);
                return Image.FromStream(ms);
            }

            return null;
        }

        return ProfilePicture;
    }

    /// <summary>
    ///     Fetches a creator's profile picture URL
    /// </summary>
    /// <returns></returns>
    public string? GetProfilePictureURL()
    {
        // Fetch the actual image URL
        var imageNode = LandingPage.DocumentNode.Descendants().FirstOrDefault(x =>
            x.HasClass("fancy-image__image") && x.Name == "img" && x.Attributes["src"].Value.Contains("icons"));

        return "https:" + imageNode.Attributes["src"].Value;
    }

    /// <summary>
    ///     Fetches a creator's profile banner
    /// </summary>
    /// <returns></returns>
    public Image? GetProfileBanner()
    {
        if (ProfileBanner == null)
        {
            // Fetch the actual image URL
            var imageNode = LandingPage.DocumentNode.Descendants().FirstOrDefault(x =>
                x.HasClass("fancy-image__image") && x.Name == "img" && x.Attributes["src"].Value.Contains("banners"));

            // HTTP Weird Stuff
            var profilePicData = HttpManager.GET("https:" + imageNode.Attributes["src"].Value);
            if (!profilePicData.Errored)
            {
                using var ms = profilePicData.Content.ReadAsStreamAsync().Result;
                ProfilePicture = Image.FromStream(ms);
                return Image.FromStream(ms);
            }

            return null;
        }

        return ProfileBanner;
    }

    /// <summary>
    ///     Fetches a creator's profile banner URL
    /// </summary>
    /// <returns></returns>
    public string? GetProfileBannerURL()
    {
        // Fetch the actual image URL
        var imageNode = LandingPage.DocumentNode.Descendants().FirstOrDefault(x =>
            x.HasClass("fancy-image__image") && x.Name == "img" && x.Attributes["src"].Value.Contains("banners"));

        return "https:" + imageNode.Attributes["src"].Value;
    }

    #endregion
}