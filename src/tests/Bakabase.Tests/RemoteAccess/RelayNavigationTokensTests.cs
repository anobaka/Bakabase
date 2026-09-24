using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The tickets that let a window switch into a relay from another origin.
/// </summary>
/// <remarks>
/// A ticket is the one thing that gets a cross-site request past a relay's guard, so
/// every way it could outlive its purpose — a second use, another relay, a late arrival —
/// is pinned here, with the clock under the test's control.
/// </remarks>
[TestClass]
public class RelayNavigationTokensTests
{
    private const int Port = 34650;

    internal sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    [TestMethod]
    public void A_fresh_ticket_opens_the_relay_it_was_minted_for()
    {
        var tickets = new RelayNavigationTokens(new ManualClock());

        Assert.IsTrue(tickets.TryConsume(tickets.Mint(Port), Port));
    }

    [TestMethod]
    public void A_ticket_is_spent_on_first_use()
    {
        // It travels in a URL, so it lands in history, in logs, on screen. A second use
        // is somebody replaying one of those.
        var tickets = new RelayNavigationTokens(new ManualClock());
        var ticket = tickets.Mint(Port);

        Assert.IsTrue(tickets.TryConsume(ticket, Port));
        Assert.IsFalse(tickets.TryConsume(ticket, Port));
    }

    [TestMethod]
    public void A_ticket_for_another_relay_is_refused_and_burned()
    {
        // Presented to the wrong relay it is refused, and spent: it has leaked out of the
        // navigation it was minted for, and nothing that follows should be able to use it.
        var tickets = new RelayNavigationTokens(new ManualClock());
        var ticket = tickets.Mint(Port);

        Assert.IsFalse(tickets.TryConsume(ticket, Port + 1));
        Assert.IsFalse(tickets.TryConsume(ticket, Port));
    }

    [TestMethod]
    public void A_ticket_that_arrives_late_is_refused()
    {
        var clock = new ManualClock();
        var tickets = new RelayNavigationTokens(clock);

        var late = tickets.Mint(Port);
        clock.Advance(RelayNavigationTokens.Lifetime);
        Assert.IsFalse(tickets.TryConsume(late, Port), "a ticket is dead the moment its lifetime ends");

        var inTime = tickets.Mint(Port);
        clock.Advance(RelayNavigationTokens.Lifetime - TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(tickets.TryConsume(inTime, Port));
    }

    [TestMethod]
    public void Nothing_that_was_not_minted_is_a_ticket()
    {
        var tickets = new RelayNavigationTokens(new ManualClock());
        var ticket = tickets.Mint(Port);

        Assert.IsFalse(tickets.TryConsume(null, Port));
        Assert.IsFalse(tickets.TryConsume("", Port));
        Assert.IsFalse(tickets.TryConsume(ticket.ToUpperInvariant(), Port), "tickets are compared exactly");
        Assert.IsFalse(tickets.TryConsume(new string('0', ticket.Length), Port));

        // None of those spent the real one.
        Assert.IsTrue(tickets.TryConsume(ticket, Port));
    }

    [TestMethod]
    public void Tickets_are_long_random_and_url_safe()
    {
        var tickets = new RelayNavigationTokens(new ManualClock());
        var minted = Enumerable.Range(0, 32).Select(_ => tickets.Mint(Port)).ToList();

        Assert.AreEqual(minted.Count, minted.Distinct().Count());

        foreach (var ticket in minted)
        {
            // 128 bits, and nothing a query string would have to escape.
            Assert.AreEqual(32, ticket.Length, ticket);
            Assert.IsTrue(ticket.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), ticket);
        }
    }

    [TestMethod]
    public void A_flood_of_tickets_evicts_the_oldest_rather_than_growing()
    {
        // Nobody switches servers dozens of times a minute. Something minting on a loop
        // must not grow the table without bound, and the tickets it evicts are the ones
        // nobody is still waiting on.
        var clock = new ManualClock();
        var tickets = new RelayNavigationTokens(clock);
        var minted = new List<string>();

        for (var i = 0; i < 65; i++)
        {
            minted.Add(tickets.Mint(Port));
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.IsFalse(tickets.TryConsume(minted[0], Port));

        foreach (var ticket in minted.Skip(1))
        {
            Assert.IsTrue(tickets.TryConsume(ticket, Port));
        }
    }
}
