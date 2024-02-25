using HtmlAgilityPack;
using Orobouros.Bases;
using Orobouros.Managers;
using Orobouros.Tools.Web;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
            DebugManager.WriteToDebugLog("[+] Page asset returned code " + asset.ResponseCode);
            DebugManager.WriteToDebugLog("[+] Creator URL: " + creator.URL);

            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(asset.Content.ReadAsStringAsync().Result);

            DebugManager.WriteToDebugLog("[+] HTML Document initialized: " + htmlDoc.Text.Length + " characters");

            // Parsing
            HtmlNode? postsContainer = HtmlManager.SelectNodesByClass(htmlDoc, "card-list__items", "div").FirstOrDefault(); // Fetch the container that holds the posts
            var count = 0;
            if (postsContainer != null)
            {
                if (postsContainer.ChildNodes != null)
                {
                    foreach (HtmlNode post in HtmlManager.FetchChildNodes(postsContainer))
                    {
                        if (count >= numberOfPostsToGet) // We want to cut off the loop when the threshold is reached
                            break;

                        DebugManager.WriteToDebugLog("[+] Post HTML type: " + post.Name);
                        HtmlNode? linkNode = post.ChildNodes.FirstOrDefault(x => x.Name == "a" && x.Attributes["href"] != null); // Find first child node with a link attribute(only "a" nodes here)

                        // Add URL to the list
                        Post posty = new Post();
                        Author auth = new Author();
                        auth.Username = creator.Name;
                        auth.URL = creator.URL;
                        auth.ProfilePicture = creator.GetProfilePictureURL();
                        posty.Author = auth;
                        posty.URL = creator.PartyDomain + linkNode?.Attributes["href"].Value;

                        DebugManager.WriteToDebugLog("[+] Post URL: " + posty.URL);

                        // Perform HTTP request
                        HttpAPIAsset? response = HttpManager.GET(posty.URL);

                        if (response.Successful == false || response.Errored)
                        {
                            continue;
                        }

                        // Load HTML
                        HtmlDocument responseDocument = new HtmlDocument();
                        responseDocument.LoadHtml(response.Content.ReadAsStringAsync().Result);

                        DebugManager.WriteToDebugLog("[+] Post HTML Document return status code " + response.ResponseCode);
                        DebugManager.WriteToDebugLog("[+] Post HTML Document Length: " + responseDocument.Text.Length);

                        // Fetch post ID
                        Regex postIDFinder = new Regex("/post/(.*)");
                        Match postIDMatch = postIDFinder.Match(posty.URL);
                        string postId = postIDMatch.Groups[1].Value;
                        if (postId != String.Empty && postId != null)
                        {
                            posty.Id = int.Parse(postId).ToString();
                        }

                        // Fetch post title
                        HtmlNode? titleParent = responseDocument.DocumentNode.Descendants().FirstOrDefault(x => x.HasClass("post__title") && x.Name == "h1");
                        HtmlNode? titleSpan = titleParent?.ChildNodes.FirstOrDefault(x => x.Name == "span");
                        string? postTitle = HttpUtility.HtmlDecode(titleSpan?.InnerText);

                        // Fetch post upload date
                        HtmlNode? dateParent = responseDocument.DocumentNode.Descendants().FirstOrDefault(x => x.HasClass("post__published") && x.Name == "div");
                        HtmlNode? divChild = dateParent?.ChildNodes.FirstOrDefault(x => x.Name == "div");
                        string? TimeText = dateParent?.InnerHtml.Replace(divChild.OuterHtml, "").Replace("\n", "").Trim();
                        DateTime uploadDate = DateTime.ParseExact(TimeText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                        posty.UploadDate = uploadDate;

                        // Handle empty post titles
                        if (postTitle == "Untitled")
                        {
                            postTitle = postTitle + " (" + uploadDate.ToFileTime() + ")";
                        }

                        posty.Title = postTitle.Trim();

                        // Text for posts
                        List<HtmlNode>? contentNodes = HtmlManager.SelectNodesByClass(responseDocument, "post__content", "div");
                        HtmlNode? contentNode = contentNodes?.FirstOrDefault();
                        if (contentNode != null)
                        {
                            // Content stuff
                            var scrDesc = contentNode.InnerText;
                            if (scrDesc.StartsWith("\n"))
                            {
                                scrDesc = scrDesc.Remove(0);
                            }
                            foreach (var child in contentNode.ChildNodes) scrDesc = scrDesc + child.InnerText + "\n";
                            posty.Description = scrDesc.Trim();
                        }

                        // Files
                        List<HtmlNode>? filesNodes = HtmlManager.SelectNodesByClass(responseDocument, "post__files", "div");
                        HtmlNode? filesNode = contentNodes?.FirstOrDefault();
                        if (filesNode != null)
                        {
                            List<HtmlNode> files = filesNode.Descendants().Where(x => x.Attributes["href"] != null && x.Attributes["download"] != null).ToList();
                            foreach (var file in files)
                            {
                                Attachment filey = new Attachment();
                                filey.URL = file.Attributes["href"].Value;
                                filey.Name = StringManager.SanitizeFile(HttpUtility.UrlDecode(file.Attributes["download"].Value));

                                if (UniAssemblyInfo.IsVideo(filey.Name))
                                {
                                    filey.AttachmentType = UniAssemblyInfo.AttachmentContent.Video;
                                }
                                else if (UniAssemblyInfo.IsImage(filey.Name))
                                {
                                    filey.AttachmentType = UniAssemblyInfo.AttachmentContent.Image;
                                }
                                else
                                {
                                    filey.AttachmentType = UniAssemblyInfo.AttachmentContent.GenericFile;
                                }

                                filey.Binary = HttpManager.GET(filey.URL).Content.ReadAsStream();
                                posty.Attachments.Add(filey);
                            }
                        }

                        // Attachment posts
                        List<HtmlNode>? attachmentNodes = HtmlManager.SelectNodesByClass(responseDocument, "post__attachments", "ul");
                        HtmlNode? attachmentNode = contentNodes?.FirstOrDefault();
                        if (attachmentNode != null)
                        {
                            List<HtmlNode> rawAttachments = attachmentNode.Descendants().Where(x => x.Attributes["href"] != null && x.Attributes["download"] != null).ToList();
                            foreach (var attachment in rawAttachments)
                            {
                                Attachment filey = new Attachment();
                                filey.URL = attachment.Attributes["href"].Value;
                                filey.Name = StringManager.SanitizeFile(HttpUtility.UrlDecode(attachment.Attributes["download"].Value));

                                if (UniAssemblyInfo.IsVideo(filey.Name))
                                {
                                    filey.AttachmentType = UniAssemblyInfo.AttachmentContent.Video;
                                }
                                else if (UniAssemblyInfo.IsImage(filey.Name))
                                {
                                    filey.AttachmentType = UniAssemblyInfo.AttachmentContent.Image;
                                }
                                else
                                {
                                    filey.AttachmentType = UniAssemblyInfo.AttachmentContent.GenericFile;
                                }

                                filey.Binary = HttpManager.GET(filey.URL).Content.ReadAsStream();
                                posty.Attachments.Add(filey);
                            }
                        }

                        // Comments
                        List<HtmlNode>? commentNodes = HtmlManager.SelectNodesByClass(responseDocument, "post__comments ", "div");
                        HtmlNode? commentNode = commentNodes?.FirstOrDefault();
                        if (commentNode != null)
                        {
                            List<HtmlNode> rawComments = HtmlManager.FetchChildNodes(commentNode);
                            foreach (var comment in rawComments)
                            {
                                HtmlNode? HeaderNode = comment.ChildNodes.First(x => x.HasClass("comment__header") && x.Name == "header");
                                HtmlNode? BodyNode = comment.ChildNodes.First(x => x.HasClass("comment__body") && x.Name == "section");
                                HtmlNode? FooterNode = comment.ChildNodes.First(x => x.HasClass("comment__footer") && x.Name == "footer");

                                Comment comm = new Comment();
                                Author authy = new Author();
                                authy.Username = HeaderNode.ChildNodes.First(x => x.Name == "a").InnerText;
                                comm.Author = authy;
                                comm.ParentPost = posty;
                                comm.Content = BodyNode.ChildNodes.First(x => x.Name == "p").InnerText;
                                comm.PostTime = DateTime.ParseExact(FooterNode.ChildNodes.First(x => x.Name == "time").InnerText, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                                posty.Comments.Add(comm);
                            }
                        }
                        postUrls.Add(posty);
                        count++;
                    }
                }
            }

            return postUrls;
        }
    }
}