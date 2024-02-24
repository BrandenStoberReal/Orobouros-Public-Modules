namespace Orobouros.PartyModule
{
    public class PageDetails
    {
        /// <summary>
        /// How many pages (increments of 50) the creator has
        /// </summary>
        public int Pages { get; private set; }

        /// <summary>
        /// How many posts are left over after all pages are calculated. This will always have a
        /// value if the total number of posts is not a multiple of 50.
        /// </summary>
        public int LeftoverPosts { get; private set; }

        /// <summary>
        /// Whether the creator has less than 50 posts and they are all on a singular page.
        /// </summary>
        public bool IsSinglePage { get; private set; }

        /// <summary>
        /// PageDetails constructor. This class is mainly for readability's sake instead of using tuples.
        /// </summary>
        /// <param name="pages"># of pages the creator has</param>
        /// <param name="posts"># of leftover posts the creator has</param>
        /// <param name="single">Whether creator has less than 50 posts</param>
        public PageDetails(int pages, int posts, bool single)
        {
            Pages = pages;
            LeftoverPosts = posts;
            IsSinglePage = single;
        }
    }
}