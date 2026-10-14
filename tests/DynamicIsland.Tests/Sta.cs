using System.Runtime.ExceptionServices;

namespace DynamicIsland.Tests;

/// <summary>WPF controls must be created on an STA thread; xUnit runs tests on MTA thread-pool threads.</summary>
internal static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }
}
