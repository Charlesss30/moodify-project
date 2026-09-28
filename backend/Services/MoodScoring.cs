namespace mood_recommendation.Services;
public static class MoodScoring
{
    public static decimal DistanceSquared(decimal v1, decimal a1, decimal v2, decimal a2) =>
        (v1 - v2) * (v1 - v2) + (a1 - a2) * (a1 - a2);
    public static double MatchPercent(decimal distanceSquared) =>
        Math.Round(100 * (1 - Math.Sqrt((double)distanceSquared) / Math.Sqrt(8)), 2);
}
