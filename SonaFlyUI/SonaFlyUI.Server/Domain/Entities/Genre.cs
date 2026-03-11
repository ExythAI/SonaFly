namespace SonaFlyUI.Server.Domain.Entities;

public class Genre : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public ICollection<TrackGenre> TrackGenres { get; set; } = new List<TrackGenre>();
}
