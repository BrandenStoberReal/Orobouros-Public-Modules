﻿using Orobouros.Attributes;
using Orobouros.Bases;
using Orobouros.Managers;
using Orobouros.PartyModule.Helpers;
using Orobouros.Tools.Web;
using static Orobouros.OrobourosInformation;

namespace Orobouros.PartyModule;

[OrobourosModule("Kemono/Coomer Module", "6b2f454a-55a5-43d5-ae2d-54fde6dc3c7d", "1.0.0.5",
    "Module for scraping kemono and coomer content.")]
public class MainModule
{
    // Websites supported by this module. You can either use loose domains ("google.com") or
    // full URLs ("https://google.com/search?q=test") depending on your code. Website URL to be
    // scraped will be checked if the URL includes this text at all.
    [ModuleSites]
    public List<string> SupportedWebsites { get; set; } = new()
    {
        "https://kemono.su",
        "https://coomer.su"
    };

    // Returned content supported by this module. Anything not explicitly listed should be
    // classified as "Other" enum type.
    [ModuleContents]
    public List<ModuleContent> SupportedContent { get; set; } = new()
    {
        ModuleContent.Subposts,
        ModuleContent.Videos,
        ModuleContent.Images,
        ModuleContent.Files
    };

    // Module methods.

    // Initializer method. Used to run code when the module loads. Always runs on a new thread.
    [ModuleInit]
    public void Initialize()
    {
    }

    // Scrape method. Called whenever the framework recieves a scrape request and this module
    // matches the requested content.
    [ModuleScrape]
    public ModuleData? Scrape(ScrapeParameters parameters)
    {
        // Initialize classes
        var data = new ModuleData();
        var Posts = new List<Post>();

        var creator = new Creator(parameters.URL);

        // Couldn't fetch creator
        if (creator.Errored || !creator.Successful) return null;

        LoggingManager.LogInformation("Creator: " + creator.Name);

        // Page data vars
        var pagesAndPosts = MathHelper.DoPageMath(creator, parameters.ScrapeInstances);
        var pages = pagesAndPosts.Pages;
        var leftoverPosts = pagesAndPosts.LeftoverPosts;
        var singlePage = pagesAndPosts.IsSinglePage;

        #region Handle each data type requested

        // Subposts media type
        if (parameters.RequestedContent.Contains(ModuleContent.Subposts))
        {
            LoggingManager.WriteToDebugLog("Subposts requested!");

            // Return subposts Full Page Scraper
            if (singlePage)
            {
                // Used if only grabbing the first page
                LoggingManager.WriteToDebugLog("Scraping single page...");
                var postsList = ParseUtility.ScrapePage(creator, 0, leftoverPosts);
                if (postsList == null)
                {
                    LoggingManager.LogError(
                        "[SINGLE-PAGE PARSER] A page failed to scrape! Are you IP banned, or are the partysites undergoing repairs? Scrape aborted.");
                }

                Posts = Posts.Concat(postsList).ToList();
                LoggingManager.LogInformation("Scraped " + leftoverPosts + " posts");
            }
            else
            {
                // Used for everything else
                for (var i = 0; i < pages; i++)
                {
                    LoggingManager.LogInformation("Scraping Page #" + (i + 1) + "/" + pages + "...");
                    LoggingManager.LogInformation($"Page #{i + 1} out of {pages} parsing");
                    var postList = ParseUtility.ScrapePage(creator, i, 50);
                    if (postList == null)
                    {
                        LoggingManager.LogError(
                            "[MULTI-SINGLE-PAGE PARSER] A page failed to scrape! Are you IP banned, or are the partysites undergoing repairs? Scrape aborted.");
                        break;
                    }

                    Posts = Posts.Concat(postList).ToList();
                }
            }

            // Partial page scraper, used for scraping the final page from the series
            if (leftoverPosts > 0 && !singlePage)
            {
                LoggingManager.WriteToDebugLog("Scraping final page...");
                LoggingManager.LogInformation($"Parsing last page with {leftoverPosts} posts");
                var leftoverPosties = ParseUtility.ScrapePage(creator, pages, leftoverPosts);
                if (leftoverPosties == null)
                {
                    LoggingManager.LogError(
                        "[FINAL-PAGE PARSER] A page failed to scrape! Are you IP banned, or are the partysites undergoing repairs? Scrape aborted.");
                }

                Posts = Posts.Concat(leftoverPosties).ToList();
            }

            // Package data for transport
            foreach (var post in Posts)
            {
                var packagedData = new ProcessedScrapeData(ModuleContent.Subposts, post.URL, post);
                data.Content.Add(packagedData);
            }
        }

        // Video parsing
        if (parameters.RequestedContent.Contains(ModuleContent.Videos))
        {
            // Scrape all posts packaged over
            if (parameters.ScrapeInstances == -1)
            {
                foreach (var post in parameters.Subposts)
                    foreach (var filey in post.Attachments)
                        if (filey.AttachmentType == AttachmentContent.Video)
                        {
                            var packagedAttachment = new ProcessedScrapeData(ModuleContent.Videos, filey.URL, filey);
                            data.Content.Add(packagedAttachment);
                        }
            }
            else
            {
                var count = 0;
                foreach (var post in parameters.Subposts)
                {
                    if (count >= parameters.ScrapeInstances) break;
                    foreach (var filey in post.Attachments)
                        if (filey.AttachmentType == AttachmentContent.Video)
                        {
                            var packagedAttachment = new ProcessedScrapeData(ModuleContent.Videos, filey.URL, filey);
                            data.Content.Add(packagedAttachment);
                            count++;
                        }
                }
            }
        }

        // Image Parsing
        if (parameters.RequestedContent.Contains(ModuleContent.Images))
        {
            // Scrape all posts packaged over
            if (parameters.ScrapeInstances == -1)
            {
                foreach (var post in parameters.Subposts)
                    foreach (var filey in post.Attachments)
                        if (filey.AttachmentType == AttachmentContent.Image)
                        {
                            var packagedAttachment = new ProcessedScrapeData(ModuleContent.Images, filey.URL, filey);
                            data.Content.Add(packagedAttachment);
                        }
            }
            else
            {
                var count = 0;
                foreach (var post in parameters.Subposts)
                {
                    if (count >= parameters.ScrapeInstances) break;
                    foreach (var filey in post.Attachments)
                        if (filey.AttachmentType == AttachmentContent.Image)
                        {
                            var packagedAttachment = new ProcessedScrapeData(ModuleContent.Images, filey.URL, filey);
                            data.Content.Add(packagedAttachment);
                            count++;
                        }
                }
            }
        }

        // File Parsing
        if (parameters.RequestedContent.Contains(ModuleContent.Files))
        {
            // Scrape all posts packaged over
            if (parameters.ScrapeInstances == -1)
            {
                foreach (var post in parameters.Subposts)
                    foreach (var filey in post.Attachments)
                        if (filey.AttachmentType == AttachmentContent.GenericFile)
                        {
                            var packagedAttachment = new ProcessedScrapeData(ModuleContent.Files, filey.URL, filey);
                            data.Content.Add(packagedAttachment);
                        }
            }
            else
            {
                var count = 0;
                foreach (var post in parameters.Subposts)
                {
                    if (count >= parameters.ScrapeInstances) break;
                    foreach (var filey in post.Attachments)
                        if (filey.AttachmentType == AttachmentContent.GenericFile)
                        {
                            var packagedAttachment = new ProcessedScrapeData(ModuleContent.Files, filey.URL, filey);
                            data.Content.Add(packagedAttachment);
                            count++;
                        }
                }
            }
        }

        #endregion Handle each data type requested

        return data;
    }
}