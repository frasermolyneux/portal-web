namespace XtremeIdiots.Portal.Web.ViewModels;

/// <summary>
/// Public-safe projection of a recent player's geolocation for the global server map.
/// Exposes only coordinates and the originating game type — never identifiers, IPs,
/// names, or player profile data.
/// </summary>
/// <param name="Lat">Latitude.</param>
/// <param name="Lng">Longitude.</param>
/// <param name="GameType">Game type identifier used to pick the marker icon.</param>
public sealed record PublicPlayerMapPointViewModel(double Lat, double Lng, string GameType);
