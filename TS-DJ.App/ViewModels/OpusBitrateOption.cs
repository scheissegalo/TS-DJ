namespace TS_DJ.App.ViewModels;

public sealed class OpusBitrateOption
{
    public OpusBitrateOption(int kbps, string label)
    {
        Kbps = kbps;
        Label = label;
    }

    public int Kbps { get; }
    public string Label { get; }

    public override string ToString() => Label;
}
