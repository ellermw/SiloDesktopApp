using System.Globalization;
using System.Runtime.CompilerServices;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

/// <summary>
/// The one way a test changes the thread's culture, and the one place the process-wide locale baseline is fixed.
///
/// <see cref="LocaleService"/> reads <c>CultureInfo.CurrentCulture</c> of whichever thread first touches it (its static
/// <c>EnglishResolution</c> and <c>Shared</c>) and keeps that for the life of the process: <c>AppStrings.FormatCulture</c> of
/// every test that does not pin its own locale. A test that sets its thread to ar-SA / de-DE / th-TH and then reaches
/// <c>new LocaleService()</c> or <c>AppStrings</c> first therefore leaves ar-SA as the process's format culture, and
/// unrelated tests format "7.5" as "7٫5" (run <c>directconn-final-r1</c>: 11 failures, one of them an English list join that
/// follows the format culture's language). Thread cultures themselves are per thread / per async flow and were always restored.
/// </summary>
internal static class TestCulture
{
    /// <summary>Touches <see cref="LocaleService"/> before any test code runs, while the thread still has the machine's own culture.</summary>
    [ModuleInitializer]
    internal static void FixProcessLocaleBaseline() => RuntimeHelpers.RunClassConstructor(typeof(LocaleService).TypeHandle);

    /// <summary>Sets the current thread's format (and, with <paramref name="ui"/>, UI) culture until the scope is disposed.</summary>
    internal static IDisposable Use(string cultureName, bool ui = false) => new Scope(new CultureInfo(cultureName), ui);

    /// <summary>Same, for a culture object (for example <see cref="CultureInfo.InvariantCulture"/>).</summary>
    internal static IDisposable Use(CultureInfo culture, bool ui = false) => new Scope(culture, ui);

    private sealed class Scope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
        private int _disposed;

        internal Scope(CultureInfo culture, bool ui)
        {
            // Touch the process baseline first: if this thread is the first to need it, it must see the machine's culture, not this one.
            RuntimeHelpers.RunClassConstructor(typeof(LocaleService).TypeHandle);
            CultureInfo.CurrentCulture = culture;
            if (ui) CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
