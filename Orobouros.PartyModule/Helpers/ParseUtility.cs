using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using Orobouros.Managers;
using Orobouros.Tools.Web;
using Attachment = Orobouros.Tools.Web.Attachment;

namespace Orobouros.PartyModule.Helpers;

public static class ParseUtility
{
    private static readonly Regex NumberChars = new("[^0-9]+");

    /// <summary>
    ///     Fetches a post from the specified post URL.
    /// </summary>
    /// <param name="creator"></param>
    /// <param name="url"></param>
    /// <returns></returns>
    private static Post? GetPost(Creator creator, string url)
    {
        // Add URL to the list
        var compiledPost = new Post();
        var postAuthor = new Author();
        postAuthor.Username = creator.Name;
        postAuthor.URL = creator.URL;
        postAuthor.ProfilePicture = creator.GetProfilePictureURL();
        compiledPost.Author = postAuthor;
        compiledPost.URL = url;
        // creator.PartyDomain + postUrlNode?.Attributes["href"].Value;

        LoggingManager.WriteToDebugLog("Post URL: " + compiledPost.URL);

        // Perform HTTP request
        var postWebResponse =
            HttpManager.GET(compiledPost.URL);

        // Error handler, retries the request 5 times before giving up.
        for (var i = 0; i < 5; i++)
            if (!postWebResponse.Successful || postWebResponse.Errored)
            {
                LoggingManager.LogWarning($"Post Fetch Retry #{i + 1}/5...");
                var rng = new Random();
                LoggingManager.LogWarning(
                    $"Post \"{compiledPost.URL}\" failed to fetch! Assuming this is throttling, waiting 6-7 seconds and retrying...");
                Thread.Sleep(rng.Next(6000, 7001)); // Sleep 3 seconds
                postWebResponse = HttpManager.GET(compiledPost.URL);
            }
            else
            {
                break;
            }

        // Handle errors
        if (!postWebResponse.Successful || postWebResponse.Errored)
        {
            if (postWebResponse.Errored)
                LoggingManager.LogWarning(
                    $"Post with URL \"{compiledPost.URL}\" failed to fetch with exception {postWebResponse.Exception.Message}. This post has been skipped.");
            if (!postWebResponse.Successful)
                LoggingManager.LogWarning(
                    $"Post with URL \"{compiledPost.URL}\" failed to fetch with status code {postWebResponse.ResponseCode}. This post has been skipped.");
            return null;
        }

        // Load HTML
        var postDocument = new HtmlDocument();
        postDocument.LoadHtml(postWebResponse.Content.ReadAsStringAsync().Result);

        LoggingManager.WriteToDebugLog("Post HTML Document return status code " +
                                       postWebResponse.ResponseCode);
        LoggingManager.WriteToDebugLog("Post HTML Document Length: " + postDocument.Text.Length);

        LoggingManager.WriteToDebugLog("Scraping post ID...");
        // Fetch post ID
        var postIDFinder = new Regex("/post/(.*)");
        var postIDMatch = postIDFinder.Match(compiledPost.URL);
        var postId = postIDMatch.Groups[1].Value;
        var sanitizedId = NumberChars.Replace(postId, "");
        compiledPost.Id = long.Parse(sanitizedId).ToString();

        LoggingManager.WriteToDebugLog("Scraping post title...");
        // Fetch post title
        var titleParent = postDocument.DocumentNode.Descendants()
            .FirstOrDefault(x => x.HasClass("post__title") && x.Name == "h1");
        var titleSpan = titleParent?.ChildNodes.FirstOrDefault(x => x.Name == "span");
        var postTitle = HttpUtility.HtmlDecode(titleSpan?.InnerText);

        LoggingManager.WriteToDebugLog("Scraping post upload date...");
        // Fetch post upload date
        var dateParent = postDocument.DocumentNode.Descendants()
            .FirstOrDefault(x => x.HasClass("post__published") && x.Name == "div");
        var divChild = dateParent?.ChildNodes.FirstOrDefault(x => x.Name == "div");
        var TimeText = dateParent?.InnerHtml.Replace(divChild.OuterHtml, "").Replace("\n", "").Trim();
        var uploadDate = DateTime.ParseExact(TimeText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        compiledPost.UploadDate = uploadDate;

        LoggingManager.WriteToDebugLog("Verifying post title integrity...");
        // Handle empty post titles
        if (postTitle == "Untitled") postTitle = postTitle + " (Post ID - " + compiledPost.Id + ")";

        compiledPost.Title = postTitle.Trim();

        // Post description/text
        var contentNodes = HtmlManager.SelectNodesByClass(postDocument, "post__content", "div");
        var contentNode = contentNodes?.FirstOrDefault();
        if (contentNode != null)
        {
            LoggingManager.WriteToDebugLog("Scraping post description...");
            // Content stuff
            var scrDesc = contentNode.InnerText;
            if (scrDesc.StartsWith("\n")) scrDesc = scrDesc.Remove(0);
            foreach (var child in contentNode.ChildNodes) scrDesc = scrDesc + child.InnerText + "\n";
            compiledPost.Description = scrDesc.Trim();
        }

        // Files
        var filesNodes = HtmlManager.SelectNodesByClass(postDocument, "post__files", "div");
        var filesNode = filesNodes?.FirstOrDefault();
        if (filesNode != null)
        {
            LoggingManager.WriteToDebugLog("Scraping post files...");
            List<HtmlNode> files = filesNode.Descendants()
                .Where(x => x.Attributes["href"] != null && x.Attributes["download"] != null).ToList();
            foreach (var fileobj in files)
            {
                var file = new Attachment();
                file.URL = fileobj.Attributes["href"].Value;
                file.Name = StringManager.SanitizeFile(
                    HttpUtility.UrlDecode(fileobj.Attributes["download"].Value));
                file.ParentPost = compiledPost;
                file.AttachmentType = OrobourosInformation.FindAttachmentType(file.Name);
                compiledPost.Attachments.Add(file);
            }
        }

        // Attachment posts
        var attachmentNodes = HtmlManager.SelectNodesByClass(postDocument, "post__attachments", "ul");
        var attachmentNode = attachmentNodes?.FirstOrDefault();
        if (attachmentNode != null)
        {
            LoggingManager.WriteToDebugLog("Scraping post attachments...");
            List<HtmlNode> rawAttachments = attachmentNode.Descendants()
                .Where(x => x.Attributes["href"] != null && x.Attributes["download"] != null).ToList();
            foreach (var attachmentobj in rawAttachments)
            {
                var attachment = new Attachment();
                attachment.URL = attachmentobj.Attributes["href"].Value;
                attachment.Name =
                    StringManager.SanitizeFile(
                        HttpUtility.UrlDecode(attachmentobj.Attributes["download"].Value));
                attachment.ParentPost = compiledPost;
                attachment.AttachmentType = OrobourosInformation.FindAttachmentType(attachment.Name);
                compiledPost.Attachments.Add(attachment);
            }
        }

        // Comments
        var rawComments = HtmlManager.SelectNodesByClass(postDocument, "comment", "article");
        foreach (var comment in rawComments)
        {
            LoggingManager.WriteToDebugLog("Scraping post comment...");
            // Fetch text container nodes
            var HeaderNode =
                comment.ChildNodes.First(x => x.HasClass("comment__header") && x.Name == "header");
            var BodyNode =
                comment.ChildNodes.First(x => x.HasClass("comment__body") && x.Name == "section");
            var FooterNode =
                comment.ChildNodes.First(x => x.HasClass("comment__footer") && x.Name == "footer");

            // Don't add comment if containers aren't found
            if (HeaderNode == null || BodyNode == null || FooterNode == null)
            {
                LoggingManager.LogWarning("Comment had invalid HTML, skipping...");
                continue;
            }

            // Select children with required information
            var UsernameNode = HeaderNode.ChildNodes.First(x => x.Name == "a");
            var DescriptionNode = BodyNode.ChildNodes.First(x => x.Name == "p");
            var DateNode = FooterNode.ChildNodes.First(x => x.Name == "time");

            // Don't add comment if information nodes aren't found
            if (UsernameNode == null || DescriptionNode == null || DateNode == null)
            {
                LoggingManager.LogWarning("Comment has no proper children, skipping...");
                continue;
            }

            // Create comment class
            var comm = new Comment();

            // Build comment author
            var authy = new Author();
            authy.Username = UsernameNode.InnerText;

            // Assign comment values
            comm.Author = authy;
            comm.ParentPost = compiledPost;
            comm.Content = DescriptionNode.InnerText;
            comm.PostTime = DateTime.ParseExact(DateNode.InnerText.Replace("\n", "").Trim(),
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            compiledPost.Comments.Add(comm);
        }

        return compiledPost;
    }

    /// <summary>
    ///     Scrapes a creator's page for posts. Each page is worth 50 posts.
    /// </summary>
    /// <param name="page"></param>
    /// <param name="numberOfPostsToGet"></param>
    /// <returns>A list of posts</returns>
    public static List<Post>? ScrapePage(Creator creator, int page, int numberOfPostsToGet)
    {
        var scrapedPosts = new List<Post>();
        var asset = HttpManager.GET(creator.URL + $"?o={page * 50}");

        // Handle ratelimiting
        if (asset.ResponseCode == HttpStatusCode.TooManyRequests)
            while (asset.ResponseCode == HttpStatusCode.TooManyRequests)
            {
                var rng = new Random();
                Thread.Sleep(rng.Next(10000, 15000));
                asset = HttpManager.GET(creator.URL + $"?o={page * 50}");
            }

        // Try to salvage any bad HTTP requests
        for (var i = 0; i < 5; i++)
            if (asset.Errored || asset.Successful == false)
            {
                LoggingManager.LogWarning($"Page Fetch Retry #{i + 1}/5...");
                var rng = new Random();
                LoggingManager.LogWarning(
                    "A page failed to fetch! Assuming this is throttling, waiting 6-7 seconds and retrying...");
                Thread.Sleep(rng.Next(6000, 7001));
                asset = HttpManager.GET(creator.URL + $"?o={page * 50}");
            }
            else
            {
                break;
            }

        // Give up after 5 retries
        if (asset.Errored || asset.Successful == false) return null;

        LoggingManager.WriteToDebugLog("Page asset returned code " + asset.ResponseCode);
        LoggingManager.WriteToDebugLog("Creator URL: " + creator.URL);

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(asset.Content.ReadAsStringAsync().Result);

        LoggingManager.WriteToDebugLog("HTML Document initialized: " + htmlDoc.Text.Length + " characters");

        // Parsing
        var postsContainers =
            HtmlManager.SelectNodesByClass(htmlDoc, "card-list__items",
                "div"); // Fetch the container that holds the posts
        var postsContainer = postsContainers.FirstOrDefault();
        var count = 0;
        if (postsContainer != null)
            if (postsContainer.ChildNodes != null)
                foreach (var postobj in HtmlManager.FetchChildNodes(postsContainer))
                {
                    if (count >= numberOfPostsToGet) // We want to cut off the loop when the threshold is reached
                        break;

                    LoggingManager.WriteToDebugLog("Post HTML type: " + postobj.Name);
                    var postUrlNode =
                        postobj.ChildNodes.FirstOrDefault(x =>
                            x.Name == "a" &&
                            x.Attributes["href"] !=
                            null); // Find first child node with a link attribute(only "a" nodes here)

                    var post = GetPost(creator, creator.PartyDomain + postUrlNode?.Attributes["href"].Value);
                    if (post != null) scrapedPosts.Add(post);
                    count++;
                }

        return scrapedPosts;
    }

    /*
    /// <summary>
    ///     Scrapes a creator's posts in a specific range.
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <param name="creator"></param>
    /// <returns></returns>
    public static List<Post>? ScrapeRange(int start, int end, Creator creator)
    {
    }
    */
}