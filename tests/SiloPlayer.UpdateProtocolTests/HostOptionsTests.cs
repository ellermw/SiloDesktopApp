using SiloPlayer.UpdateHost;

namespace SiloPlayer.UpdateProtocolTests;

public sealed class HostOptionsTests
{
    private static string[] Arguments(string tag) =>
        ["--worker", "no", "--app-pipe", "SiloUpdate-" + new string('a', 32),
         "--worker-pipe", "SiloUpdate-" + new string('b', 32), "--parent", "123",
         "--parent-start", "456", "--package", Path.GetTempPath(), "--tag", tag,
         "--restart", "yes", "--owner", "0"];

    [Theory]
    [InlineData("v1.2.518-beta.1")]
    [InlineData("v1.2.518-beta.12")]
    [InlineData("v1.2.518-beta.65534")]
    [InlineData("v1.2.518")]
    [InlineData("v1.2.518.0")]
    public void App_to_broker_to_worker_preserves_release_tag(string tag)
    {
        var broker = HostOptions.Parse(Arguments(tag));
        var worker = HostOptions.Parse(broker.Arguments(true, 789, 101112).ToArray());
        Assert.Equal(tag, broker.Tag);
        Assert.Equal(tag, worker.Tag);
        Assert.True(worker.Worker);
    }

    [Fact]
    public void Window_waits_for_the_client_download_only_when_asked_and_never_in_the_worker()
    {
        Assert.False(HostOptions.Parse(Arguments("v1.2.518-beta.1")).AwaitDownload);
        var broker = HostOptions.Parse([.. Arguments("v1.2.518-beta.1"), "--await-download", "yes"]);
        Assert.True(broker.AwaitDownload);
        // The elevated worker starts only after the download; its arguments never carry the flag.
        var worker = broker.Arguments(true, 789, 101112).ToArray();
        Assert.DoesNotContain("--await-download", worker);
        Assert.False(HostOptions.Parse(worker).AwaitDownload);
        Assert.False(HostOptions.Parse([.. Arguments("v1.2.518-beta.1"), "--await-download", "no"]).AwaitDownload);
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse([.. Arguments("v1.2.518-beta.1"), "--await-download", "maybe"]));
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse([.. worker, "--await-download", "yes"]));
    }

    [Fact]
    public void Only_a_downloading_window_asks_restart_now_or_later()
    {
        var download = (string[])[.. Arguments("v1.2.518-beta.1"), "--await-download", "yes"];
        Assert.False(HostOptions.Parse(download).ConfirmRestart);
        var asking = HostOptions.Parse([.. download, "--confirm-restart", "yes"]);
        Assert.True(asking.ConfirmRestart);
        Assert.False(HostOptions.Parse([.. download, "--confirm-restart", "no"]).ConfirmRestart);
        // The elevated worker starts after the answer and never carries the flag.
        var worker = asking.Arguments(true, 789, 101112).ToArray();
        Assert.DoesNotContain("--confirm-restart", worker);
        Assert.False(HostOptions.Parse(worker).ConfirmRestart);
        // Without the client's download there is nothing to ask about.
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse([.. Arguments("v1.2.518-beta.1"), "--confirm-restart", "yes"]));
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse([.. download, "--confirm-restart", "later"]));
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse([.. worker, "--confirm-restart", "yes"]));
    }

    [Theory]
    [InlineData("v1.2.517.1")]
    [InlineData("v1.2.517.12")]
    [InlineData("1.2.517.12")]
    [InlineData("v1.2.517.65534")]
    public void Stable_tags_cannot_use_revisions_reserved_for_beta(string tag)
        => Assert.Throws<InvalidDataException>(() => HostOptions.Parse(Arguments(tag)));

    [Theory]
    [InlineData("1.2.518")]
    [InlineData("1.2.518.0")]
    [InlineData("1.2.518-beta.12")]
    public void Bare_tags_are_rejected_before_broker_or_worker_startup(string tag)
    {
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse(Arguments(tag)));
        var worker = Arguments(tag); worker[1] = "yes";
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse(worker));
    }

    [Theory]
    [InlineData("v1.2.518-beta.0")]
    [InlineData("v1.2.518-beta.01")]
    [InlineData("v1.2.518-beta.65535")]
    [InlineData("v1.2.0-beta.1")]
    [InlineData("v1.2.518-beta.1+repacked")]
    [InlineData("v1.2.518-rc.1")]
    [InlineData("v1.2.518-beta.1\n")]
    public void Unsupported_beta_tags_are_rejected(string tag)
        => Assert.Throws<InvalidDataException>(() => HostOptions.Parse(Arguments(tag)));
}
