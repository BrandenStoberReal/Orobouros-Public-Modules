using Orobouros.Attributes;
using Orobouros.Bases;
using Orobouros.Managers;
using Orobouros.PartyModule.Helpers;
using Orobouros.Tools.Web;
using static Orobouros.OrobourosInformation;
using Attachment = Orobouros.Tools.Web.Attachment;

namespace Orobouros.PartyModule
{
    [OrobourosModule("Kemono/Coomer Module", "6b2f454a-55a5-43d5-ae2d-54fde6dc3c7d", "1.0.0.2", "Module for scraping kemono and coomer content.")]
    public class MainModule
    {
        // Websites supported by this module. You can either use loose domains ("google.com") or
        // full URLs ("https://google.com/search?q=test") depending on your code. Website URL to be
        // scraped will be checked if the URL includes this URL at all.
        [ModuleSites]
        public List<string> SupportedWebsites { get; set; } = new List<string>
        {
            "https://kemono.su",
            "https://coomer.su"
        };

        // Returned content supported by this module. Anything not explicitly listed should be
        // classified as "Other" enum type.
        [ModuleContents]
        public List<ModuleContent> SupportedContent { get; set; } = new List<ModuleContent>
        {
            ModuleContent.Subposts,
            ModuleContent.Videos,
            ModuleContent.Images,
            ModuleContent.Files
        };

        // Module methods.

        // Initializer method. Used to run code when the module loads. Always run on a new thread.
        [ModuleInit]
        public void Initialize()
        {
        }

        // Scrape method. Called whenever the framework recieves a scrape request and this module
        // matches the requested content.
        [ModuleScrape]
        public ModuleData? Scrape(ScrapeParameters parameters)
        {
            ModuleData data = new ModuleData();
            var Posts = new List<Post>();

            Creator creator = new Creator(parameters.URL);

            if (creator.Errored || !creator.Successful)
            {
                return null;
            }

            LoggingManager.LogInformation("Creator: " + creator.Name);

            // Page data vars
            var pagesAndPosts = MathHelper.DoPageMath(creator, parameters.ScrapeInstances);
            var pages = pagesAndPosts.Pages;
            var leftoverPosts = pagesAndPosts.LeftoverPosts;
            var singlePage = pagesAndPosts.IsSinglePage;

            // Subposts media type
            if (parameters.RequestedContent.Count == 1 && parameters.RequestedContent.First() == ModuleContent.Subposts)
            {
                LoggingManager.WriteToDebugLog("Subposts requested!");

                // Return subposts Full Page Scraper
                if (singlePage)
                {
                    // Used if only grabbing the first page
                    LoggingManager.WriteToDebugLog("Scraping single page...");
                    List<Post>? postsList = ParseUtility.ScrapePage(creator, 0, leftoverPosts);
                    if (postsList == null)
                    {
                        LoggingManager.LogError("[SINGLE-PAGE PARSER] A page failed to scrape! Are you IP banned, or are the partysites undergoing repairs? Scrape aborted.");
                        return null;
                    }
                    Posts = Posts.Concat(postsList).ToList();
                    LoggingManager.LogInformation("Scraped " + leftoverPosts + " posts");
                }
                else
                {
                    // Used for everything else
                    bool error = false;
                    for (var i = 0; i < pages; i++)
                    {
                        LoggingManager.LogInformation("Scraping Page #" + (i + 1) + "/" + pages + "...");
                        LoggingManager.LogInformation($"Page #{i + 1} out of {pages} parsing");
                        List<Post>? postList = ParseUtility.ScrapePage(creator, i, 50);
                        if (postList == null)
                        {
                            LoggingManager.LogError("[MULTI-SINGLE-PAGE PARSER] A page failed to scrape! Are you IP banned, or are the partysites undergoing repairs? Scrape aborted.");
                            error = true;
                            break;
                        }
                        Posts = Posts.Concat(postList).ToList();
                    }
                    if (error)
                    {
                        return null;
                    }
                }

                // Partial page scraper, used for scraping the final page from the series
                if (leftoverPosts > 0 && !singlePage)
                {
                    LoggingManager.WriteToDebugLog("Scraping final page...");
                    LoggingManager.LogInformation($"Parsing last page with {leftoverPosts} posts");
                    List<Post>? leftoverPosties = ParseUtility.ScrapePage(creator, pages, leftoverPosts);
                    if (leftoverPosties == null)
                    {
                        LoggingManager.LogError("[FINAL-PAGE PARSER] A page failed to scrape! Are you IP banned, or are the partysites undergoing repairs? Scrape aborted.");
                        return null;
                    }
                    Posts = Posts.Concat(leftoverPosties).ToList();
                }

                // Package data for transport
                foreach (Post post in Posts)
                {
                    ProcessedScrapeData packagedData = new ProcessedScrapeData(ModuleContent.Subposts, post.URL, post);
                    data.Content.Add(packagedData);
                }

                // Return subposts and do not continue
                return data;
            }

            // Video parsing
            if (parameters.RequestedContent.Contains(ModuleContent.Videos))
            {
                // Scrape all posts packaged over
                if (parameters.ScrapeInstances == -1)
                {
                    foreach (Post post in parameters.Subposts)
                    {
                        foreach (Attachment filey in post.Attachments)
                        {
                            if (filey.AttachmentType == AttachmentContent.Video)
                            {
                                ProcessedScrapeData packagedAttachment = new ProcessedScrapeData(ModuleContent.Videos, filey.URL, filey);
                                data.Content.Add(packagedAttachment);
                            }
                        }
                    }
                }
                else
                {
                    int count = 0;
                    foreach (Post post in parameters.Subposts)
                    {
                        if (count >= parameters.ScrapeInstances)
                        {
                            break;
                        }
                        foreach (Attachment filey in post.Attachments)
                        {
                            if (filey.AttachmentType == AttachmentContent.Video)
                            {
                                ProcessedScrapeData packagedAttachment = new ProcessedScrapeData(ModuleContent.Videos, filey.URL, filey);
                                data.Content.Add(packagedAttachment);
                                count++;
                            }
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
                    foreach (Post post in parameters.Subposts)
                    {
                        foreach (Attachment filey in post.Attachments)
                        {
                            if (filey.AttachmentType == AttachmentContent.Image)
                            {
                                ProcessedScrapeData packagedAttachment = new ProcessedScrapeData(ModuleContent.Images, filey.URL, filey);
                                data.Content.Add(packagedAttachment);
                            }
                        }
                    }
                }
                else
                {
                    int count = 0;
                    foreach (Post post in parameters.Subposts)
                    {
                        if (count >= parameters.ScrapeInstances)
                        {
                            break;
                        }
                        foreach (Attachment filey in post.Attachments)
                        {
                            if (filey.AttachmentType == AttachmentContent.Image)
                            {
                                ProcessedScrapeData packagedAttachment = new ProcessedScrapeData(ModuleContent.Images, filey.URL, filey);
                                data.Content.Add(packagedAttachment);
                                count++;
                            }
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
                    foreach (Post post in parameters.Subposts)
                    {
                        foreach (Attachment filey in post.Attachments)
                        {
                            if (filey.AttachmentType == AttachmentContent.GenericFile)
                            {
                                ProcessedScrapeData packagedAttachment = new ProcessedScrapeData(ModuleContent.Files, filey.URL, filey);
                                data.Content.Add(packagedAttachment);
                            }
                        }
                    }
                }
                else
                {
                    int count = 0;
                    foreach (Post post in parameters.Subposts)
                    {
                        if (count >= parameters.ScrapeInstances)
                        {
                            break;
                        }
                        foreach (Attachment filey in post.Attachments)
                        {
                            if (filey.AttachmentType == AttachmentContent.GenericFile)
                            {
                                ProcessedScrapeData packagedAttachment = new ProcessedScrapeData(ModuleContent.Files, filey.URL, filey);
                                data.Content.Add(packagedAttachment);
                                count++;
                            }
                        }
                    }
                }
            }
            return data;
        }
    }
}