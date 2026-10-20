public sealed class UpdateFixtureMarker;

public sealed class FixtureRecoveryAdapter
{
    public string Preflight(string context) => "ok";
    public string Activate(string context) => "ok";
    public string PrepareSave(string context) => "ok";
    public string CompleteSave(string context) => "ok";
    public string Suspend(string context) => "ok";
    public string Restore(string context) => "ok";
    public string CleanupFailure(string context) => "ok";
}
