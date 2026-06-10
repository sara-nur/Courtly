namespace Courtly.Domain.Enums;

/// <summary>Coarse time-of-day grouping used for slot display (Morning/Afternoon/Evening) and as a recommender signal. Stored as int.</summary>
public enum TimeOfDayBucket
{
    Morning = 0,
    Afternoon = 1,
    Evening = 2,
}
