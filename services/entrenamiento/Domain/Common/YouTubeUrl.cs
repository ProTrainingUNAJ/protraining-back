using Domain.Exceptions;
using System.Text.RegularExpressions;


namespace Domain.Common;

public record YouTubeUrl
{
    private static readonly Regex Regex = new( @"(?:youtube\.com\/(?:watch\?.*v=|embed\/|shorts\/)|youtu\.be\/)([a-zA-Z0-9_-]{11})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public string Value { get; }
    public string VideoId { get; }

    public YouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("La URL del video es obligatoria.");

        var match = Regex.Match(url.Trim());

        if (!match.Success)
            throw new DomainException("El link tiene que ser un video de YouTube.");

        VideoId = match.Groups[1].Value;

        Value = $"https://www.youtube.com/watch?v={VideoId}";
    }

    public static implicit operator string(YouTubeUrl url) => url.Value;
    public override string ToString() => Value;
}
