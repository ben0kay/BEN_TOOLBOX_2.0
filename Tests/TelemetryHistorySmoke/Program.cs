using BEN_TOOLBOX_2_0.Services;

static void Assert(bool ok, string reason)
{
    if (!ok) throw new Exception(reason);
}

var h = new TelemetryHistory(3);
Assert(h.Count == 0 && h.ValidCount == 0, "initial state");
h.Add(15);
h.Add(null);
h.Add(200);
Assert(h.Count == 3 && h.ValidCount == 2, "gap and capacity");
Assert(h.GetOldestFirst(0) == 15 && h.GetOldestFirst(1) is null
    && h.GetOldestFirst(2) == 100, "clamping and order");
h.Add(-10);
Assert(h.Count == 3 && h.ValidCount == 2, "bounded wrap");
Assert(h.GetOldestFirst(0) is null && h.GetOldestFirst(1) == 100
    && h.GetOldestFirst(2) == 0, "newest value on overwrite");
h.Add(double.NaN);
h.Add(double.PositiveInfinity);
Assert(h.Count == 3 && h.ValidCount == 1, "invalid data becomes a gap");
Assert(h.GetOldestFirst(0) == 0 && h.GetOldestFirst(1) is null
    && h.GetOldestFirst(2) is null, "gap position");
h.Clear();
Assert(h.Count == 0 && h.ValidCount == 0, "clear");
h.Add(61);
Assert(h.GetOldestFirst(0) == 61 && h.Count == 1, "reuse");
Console.WriteLine("TelemetryHistory smoke tests passed.");
