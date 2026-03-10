// HvH-StatsTracker
// Copyright (c) 2025 AuditExpert GmbH
// All rights reserved.

using HvH_StatsTracker.Shared;

namespace HvH_StatsTracker.Features;

public class MatchManager : IMatchManager
{
    private readonly EventCollector _events;

    public MatchManager(EventCollector events)
    {
        _events = events;
    }

    public void SetMatchId(Guid matchId)
    {
        _events.SetMatchId(matchId);
    }
}
