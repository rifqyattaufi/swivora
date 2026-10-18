using Swivora.Core;

// Touches real virtual desktops (the screen flickers for a moment). Does nothing on unsupported Windows builds.
[Collection("gui")]
public class DesktopBackendTests
{
    [Fact]
    public void CreateGoToRemove_WorksByGuid_AndRestoresState()
    {
        var backend = VdaDesktopBackend.Create(out var unsupported);
        if (unsupported is not null) return; // degraded mode is covered below

        var origin = backend.Current();
        var created = backend.Create();
        try
        {
            Assert.True(backend.Exists(created));
            Assert.NotEqual(origin, created);
            backend.GoTo(created);
            Assert.Equal(created, backend.Current());
            backend.GoTo(origin);
            Assert.Equal(origin, backend.Current());
        }
        finally
        {
            if (backend.Exists(created)) backend.Remove(created, origin);
        }
        Assert.False(backend.Exists(created));
        Assert.Equal(origin, backend.Current());
    }

    [Fact]
    public void Guid_StaysValid_WhenAnEarlierDesktopIsRemoved()
    {
        var backend = VdaDesktopBackend.Create(out var unsupported);
        if (unsupported is not null) return;

        var origin = backend.Current();
        var first = backend.Create();
        var second = backend.Create();
        try
        {
            backend.Remove(first, origin); // second's index shifts down
            Assert.True(backend.Exists(second));
            backend.GoTo(second);
            Assert.Equal(second, backend.Current());
            backend.GoTo(origin);
        }
        finally
        {
            foreach (var d in new[] { first, second }) if (backend.Exists(d)) backend.Remove(d, origin);
        }
    }

    [Fact]
    public void NullBackend_IsHarmless()
    {
        IDesktopBackend backend = new NullDesktopBackend("test");
        Assert.False(backend.IsAvailable);
        var d = backend.Create();
        backend.GoTo(d);
        backend.Move(IntPtr.Zero, d);
        backend.Remove(d, Guid.Empty);
        Assert.True(backend.Exists(d));
    }
}
