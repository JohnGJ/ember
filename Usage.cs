namespace Ember;

sealed record Limit(string Name, double Percent, DateTimeOffset ResetsAt);

sealed record Usage(Limit? Week, IReadOnlyList<Limit> Scoped, Limit? Session, DateTimeOffset FetchedAt)
{
    public IEnumerable<Limit> Limits
    {
        get
        {
            if (Week is not null) yield return Week;
            foreach (var limit in Scoped) yield return limit;
            if (Session is not null) yield return Session;
        }
    }
}
