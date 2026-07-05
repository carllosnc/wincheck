namespace WinCheck.Models;

public class WinProcess
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long WorkingSetBytes { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public ProcessClassification Classification { get; set; }
    public bool IsResponding { get; set; }

    public bool IsWindows => Classification < ProcessClassification.ThirdParty;
}
