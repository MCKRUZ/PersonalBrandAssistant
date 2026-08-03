namespace PBA.Domain.Enums;

public enum ContentType
{
    Blog,
    Tweet,
    LinkedInPost,

    // A short video clip authored outside PBA (ai-video-producer renders it and owns its caption).
    // Appended last on purpose: the column is an int, so existing rows keep their values.
    SocialClip
}
