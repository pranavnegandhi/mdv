using Xunit;

namespace mdv.Tests;

/// <summary>
/// xUnit collection definition for tests that require an STA (Single-Threaded Apartment) thread.
/// WPF components like Image and TextBlock require STA to be created.
/// </summary>
[CollectionDefinition("STA Tests", DisableParallelization = true)]
public sealed class STATestCollection
{
}

/// <summary>
/// Helper for running code on an STA thread, required for WPF component creation in tests.
/// </summary>
public static class STAHelper
{
    public static void RunOnSTA(Action action)
    {
        Exception? exception = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
            throw exception;
    }
}
