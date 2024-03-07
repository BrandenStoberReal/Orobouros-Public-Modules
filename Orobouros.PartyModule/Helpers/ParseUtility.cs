using HtmlAgilityPack;
using Orobouros.Bases;
using Orobouros.Managers;
using Orobouros.Tools.Web;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using Attachment = Orobouros.Tools.Web.Attachment;

namespace Orobouros.PartyModule.Helpers
{
    public static class ParseUtility
    {
        /// <summary>
        /// Scrapes a creator's page for posts.
        /// </summary>
        /// <param name="page"></param>
        /// <param name="numberOfPostsToGet"></param>
        /// <returns>A list of posts</returns>
        public static List<Post>? ScrapePage(Creator creator, int page, int numberOfPostsToGet)
        {
            var postUrls = new List<Post>();
            HttpAPIAsset asset = HttpManager.GET(creator.URL + $"?o={page * 50}");
            if (asset.Errored || asset.Successful == false)
            {
                return null;
            }
            LoggingManager.WriteToDebugLog("Page asset returned code " + asset.ResponseCode);
            LoggingManager.WriteToDebugLog("Creator URL: " + creator.URL);

            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(asset.Content.ReadAsStringAsync().Result);

            LoggingManager.WriteToDebugLog("HTML Document initialized: " + htmlDoc.Text.Length + " characters");

            // Parsing
            List<HtmlNode>? postsContainers = HtmlManager.SelectNodesByClass(htmlDoc, "card-list__items", "div"); // Fetch the container that holds the posts
            HtmlNode? postsContainer = postsContainers.FirstOrDefault();
            var count = 0;
            if (postsContainer != null)
            {
                if (postsContainer.ChildNodes != null)
                {
                    foreach (HtmlNode postobj in HtmlManager.FetchChildNodes(postsContainer))
                    {
                        if (count >= numberOfPostsToGet) // We want to cut off the loop when the threshold is reached
                            break;

                        LoggingManager.WriteToDebugLog("Post HTML type: " + postobj.Name);
                        HtmlNode? postUrlNode = postobj.ChildNodes.FirstOrDefault(x => x.Name == "a" && x.Attributes["href"] != null); // Find first child node with a link attribute(only "a" nodes here)

                        // Add URL to the list
                        Post compiledPost = new Post();
                        Author postAuthor = new Author();
                        postAuthor.Username = creator.Name;
                        postAuthor.URL = creator.URL;
                        postAuthor.ProfilePicture = creator.GetProfilePictureURL();
                        compiledPost.Author = postAuthor;
                        compiledPost.URL = creator.PartyDomain + postUrlNode?.Attributes["href"].Value;

                        LoggingManager.WriteToDebugLog("Post URL: " + compiledPost.URL);

                        // Perform HTTP request
                        HttpAPIAsset? postWebResponse = HttpManager.GET(compiledPost.URL);

                        if (!postWebResponse.Successful || postWebResponse.Errored)
                        {
                            if (postWebResponse.Errored)
                            {
                                LoggingManager.LogWarning($"Post with URL \"{compiledPost.URL}\" failed to fetch with exception {postWebResponse.Exception.Message}. This post has been skipped.");
                            }
                            if (!postWebResponse.Successful)
                            {
                                LoggingManager.LogWarning($"Post with URL \"{compiledPost.URL}\" failed to fetch with status code {postWebResponse.ResponseCode}. This post has been skipped.");
                            }
                            continue;
                        }

                        // Load HTML
                        HtmlDocument postDocument = new HtmlDocument();
                        postDocument.LoadHtml(postWebResponse.Content.ReadAsStringAsync().Result);

                        LoggingManager.WriteToDebugLog("Post HTML Document return status code " + postWebResponse.ResponseCode);
                        LoggingManager.WriteToDebugLog("Post HTML Document Length: " + postDocument.Text.Length);

                        LoggingManager.WriteToDebugLog("Scraping post ID...");
                        // Fetch post ID
                        Regex postIDFinder = new Regex("/post/(.*)");
                        Match postIDMatch = postIDFinder.Match(compiledPost.URL);
                        string postId = postIDMatch.Groups[1].Value;
                        compiledPost.Id = Int64.Parse(postId).ToString();

                        LoggingManager.WriteToDebugLog("Scraping post title...");
                        // Fetch post title
                        HtmlNode? titleParent = postDocument.DocumentNode.Descendants().FirstOrDefault(x => x.HasClass("post__title") && x.Name == "h1");
                        HtmlNode? titleSpan = titleParent?.ChildNodes.FirstOrDefault(x => x.Name == "span");
                        string? postTitle = HttpUtility.HtmlDecode(titleSpan?.InnerText);

                        LoggingManager.WriteToDebugLog("Scraping post upload date...");
                        // Fetch post upload date
                        HtmlNode? dateParent = postDocument.DocumentNode.Descendants().FirstOrDefault(x => x.HasClass("post__published") && x.Name == "div");
                        HtmlNode? divChild = dateParent?.ChildNodes.FirstOrDefault(x => x.Name == "div");
                        string? TimeText = dateParent?.InnerHtml.Replace(divChild.OuterHtml, "").Replace("\n", "").Trim();
                        DateTime uploadDate = DateTime.ParseExact(TimeText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        compiledPost.UploadDate = uploadDate;

                        LoggingManager.WriteToDebugLog("Verifying post title integrity...");
                        // Handle empty post titles
                        if (postTitle == "Untitled")
                        {
                            postTitle = postTitle + " (Post ID - " + compiledPost.Id + ")";
                        }

                        compiledPost.Title = postTitle.Trim();

                        // Post description/text
                        List<HtmlNode>? contentNodes = HtmlManager.SelectNodesByClass(postDocument, "post__content", "div");
                        HtmlNode? contentNode = contentNodes?.FirstOrDefault();
                        if (contentNode != null)
                        {
                            LoggingManager.WriteToDebugLog("Scraping post description...");
                            // Content stuff
                            var scrDesc = contentNode.InnerText;
                            if (scrDesc.StartsWith("\n"))
                            {
                                scrDesc = scrDesc.Remove(0);
                            }
                            foreach (var child in contentNode.ChildNodes) scrDesc = scrDesc + child.InnerText + "\n";
                            compiledPost.Description = scrDesc.Trim();
                        }

                        // Files
                        List<HtmlNode>? filesNodes = HtmlManager.SelectNodesByClass(postDocument, "post__files", "div");
                        HtmlNode? filesNode = filesNodes?.FirstOrDefault();
                        if (filesNode != null)
                        {
                            LoggingManager.WriteToDebugLog("Scraping post files...");
                            List<HtmlNode> files = filesNode.Descendants().Where(x => x.Attributes["href"] != null && x.Attributes["download"] != null).ToList();
                            foreach (HtmlNode fileobj in files)
                            {
                                Attachment file = new Attachment();
                                file.URL = fileobj.Attributes["href"].Value;
                                file.Name = StringManager.SanitizeFile(HttpUtility.UrlDecode(fileobj.Attributes["download"].Value));
                                file.ParentPost = compiledPost;
                                file.AttachmentType = OrobourosInformation.FindAttachmentType(file.Name);
                                compiledPost.Attachments.Add(file);
                            }
                        }

                        // Attachment posts
                        List<HtmlNode>? attachmentNodes = HtmlManager.SelectNodesByClass(postDocument, "post__attachments", "ul");
                        HtmlNode? attachmentNode = attachmentNodes?.FirstOrDefault();
                        if (attachmentNode != null)
                        {
                            LoggingManager.WriteToDebugLog("Scraping post attachments...");
                            List<HtmlNode> rawAttachments = attachmentNode.Descendants().Where(x => x.Attributes["href"] != null && x.Attributes["download"] != null).ToList();
                            foreach (HtmlNode attachmentobj in rawAttachments)
                            {
                                Attachment attachment = new Attachment();
                                attachment.URL = attachmentobj.Attributes["href"].Value;
                                attachment.Name = StringManager.SanitizeFile(HttpUtility.UrlDecode(attachmentobj.Attributes["download"].Value));
                                attachment.ParentPost = compiledPost;
                                attachment.AttachmentType = OrobourosInformation.FindAttachmentType(attachment.Name);
                                compiledPost.Attachments.Add(attachment);
                            }
                        }

                        // Comments
                        List<HtmlNode> rawComments = HtmlManager.SelectNodesByClass(postDocument, "comment", "article");
                        foreach (var comment in rawComments)
                        {
                            LoggingManager.WriteToDebugLog("Scraping post comment...");
                            // Fetch text container nodes
                            HtmlNode? HeaderNode = comment.ChildNodes.First(x => x.HasClass("comment__header") && x.Name == "header");
                            HtmlNode? BodyNode = comment.ChildNodes.First(x => x.HasClass("comment__body") && x.Name == "section");
                            HtmlNode? FooterNode = comment.ChildNodes.First(x => x.HasClass("comment__footer") && x.Name == "footer");

                            // Don't add comment if containers aren't found
                            if (HeaderNode == null || BodyNode == null || FooterNode == null)
                            {
                                LoggingManager.LogWarning("Comment had invalid HTML, skipping...");
                                continue;
                            }

                            // Select children with required information
                            HtmlNode? UsernameNode = HeaderNode.ChildNodes.First(x => x.Name == "a");
                            HtmlNode? DescriptionNode = BodyNode.ChildNodes.First(x => x.Name == "p");
                            HtmlNode? DateNode = FooterNode.ChildNodes.First(x => x.Name == "time");

                            // Don't add comment if information nodes aren't found
                            if (UsernameNode == null || DescriptionNode == null || DateNode == null)
                            {
                                LoggingManager.LogWarning("Comment has no proper children, skipping...");
                                continue;
                            }

                            // Create comment class
                            Comment comm = new Comment();

                            // Build comment author
                            Author authy = new Author();
                            authy.Username = UsernameNode.InnerText;

                            // Assign comment values
                            comm.Author = authy;
                            comm.ParentPost = compiledPost;
                            comm.Content = DescriptionNode.InnerText;
                            comm.PostTime = DateTime.ParseExact(DateNode.InnerText.Replace("\n", "").Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                            compiledPost.Comments.Add(comm);
                        }
                        postUrls.Add(compiledPost);
                        count++;
                    }
                }
            }

            return postUrls;
        }
    }
}