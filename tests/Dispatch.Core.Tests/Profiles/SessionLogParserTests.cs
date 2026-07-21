using Dispatch.Core.Profiles;
using FluentAssertions;
using Xunit;

namespace Dispatch.Core.Tests.Profiles;

/// <summary>
/// The session parser turns an LSPDFR log into one recorded shift. The duration comes
/// from the log's timestamps and is reliable; the event counts are deliberately
/// conservative and documented as best-effort, so these tests pin the behaviour that
/// matters: a real shift is recorded, an idle log is not, and the duration is right.
/// </summary>
public sealed class SessionLogParserTests
{
    private static readonly DateTimeOffset Ended = new(2026, 7, 20, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_log_with_no_on_duty_time_records_nothing()
    {
        var parser = new SessionLogParser();

        var session = parser.Parse("[19:00:00.000] LSPDFR loaded. Player idled in the menu.", Ended);

        session.Should().BeNull("the officer never went on duty");
    }

    [Fact]
    public void Duration_is_measured_between_the_first_and_last_timestamp()
    {
        var log =
            "[19:00:00.000] Going on duty\n" +
            "[19:30:00.000] Something happened\n" +
            "[20:00:00.000] Going off duty";

        var session = new SessionLogParser().Parse(log, Ended);

        session.Should().NotBeNull();
        session!.Minutes.Should().Be(60);
    }

    [Fact]
    public void A_shift_past_midnight_wraps_forward_rather_than_going_negative()
    {
        var log =
            "[23:30:00.000] Going on duty\n" +
            "[00:30:00.000] Going off duty";

        var session = new SessionLogParser().Parse(log, Ended);

        session!.Minutes.Should().Be(60, "a shift that crosses midnight is an hour, not minus 23");
    }

    [Fact]
    public void Recognised_events_are_counted()
    {
        var log =
            "[19:00:00.000] Going on duty\n" +
            "[19:05:00.000] Starting callout: Traffic Accident\n" +
            "[19:20:00.000] The suspect has been arrested\n" +
            "[19:35:00.000] A pursuit has started\n" +
            "[19:50:00.000] Issued a citation to the driver\n" +
            "[20:00:00.000] Going off duty";

        var session = new SessionLogParser().Parse(log, Ended);

        session!.Callouts.Should().Be(1);
        session.Arrests.Should().Be(1);
        session.Pursuits.Should().Be(1);
        session.Citations.Should().Be(1);
    }

    [Fact]
    public void The_ended_time_passed_in_is_kept()
    {
        var session = new SessionLogParser().Parse("[19:00:00.000] Going on duty\n[20:00:00.000] end", Ended);

        session!.EndedAt.Should().Be(Ended);
    }
}
