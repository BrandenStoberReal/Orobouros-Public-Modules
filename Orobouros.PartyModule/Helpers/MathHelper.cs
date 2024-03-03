// ReSharper disable PossibleLossOfFraction

using Orobouros.Managers;

namespace Orobouros.PartyModule.Helpers;

public static class MathHelper
{
    /// <summary>
    /// Random number generator
    /// </summary>
    public static Random RandomGenerator = new();

    /// <summary>
    /// Does math to separate pages from a number of posts. 0 can be input to process all of the
    /// creator's posts
    /// </summary>
    /// <returns>A PageDetails class</returns>
    public static PageDetails DoPageMath(Creator creator, int totalRequestedPosts)
    {
        if (totalRequestedPosts != -1)
        {
            // Posts integer is a defined value, so separation code goes here
            if (totalRequestedPosts <= 50)
            {
                // Posts integer only requires 1 page
                return new PageDetails(0, totalRequestedPosts, true);
            }

            // Posts integer requires more than 1 page
            return new PageDetails((int)Math.Floor((float)(totalRequestedPosts / 50)), totalRequestedPosts % 50, false);
        }

        var totalPosts = creator.GetTotalPosts();
        if (totalPosts != null)
        {
            return new PageDetails((int)Math.Floor((float)(totalPosts / 50)), (int)(totalPosts % 50), false);
        }

        // Total posts element doesn't appear if there is only 1 page, so that logic is handled here
        var posts = HtmlManager.FetchChildNodes(HtmlManager.FetchNodeByXPath(creator.LandingPage, "/html/body/div[2]/main/section/div[3]/div[2]"));

        return new PageDetails(0, posts.Count, true);
    }

    /// <summary>
    /// Generates a random number between the two specified values, INCLUDING the highest value.
    /// </summary>
    /// <param name="lowest"></param>
    /// <param name="highest"></param>
    /// <returns></returns>
    public static int GenerateRandomNumber(int lowest, int highest)
    {
        return RandomGenerator.Next(lowest, highest + 1);
    }
}