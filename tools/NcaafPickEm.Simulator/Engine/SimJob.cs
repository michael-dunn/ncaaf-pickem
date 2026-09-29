namespace NcaafPickEm.Simulator.Engine;

/// <summary>One of the app's jobs, as the dashboard offers it.</summary>
/// <param name="Name">The job's own <c>Name</c>, which is also what <see cref="JobDesk.RunAsync"/> takes.</param>
/// <param name="When">When the app runs it on its own, e.g. "Fri 8:00 PM ET".</param>
/// <param name="What">What running it now does.</param>
public sealed record SimJob(string Name, string When, string What);
