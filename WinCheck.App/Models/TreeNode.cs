using System.Collections.ObjectModel;
using WinCheck.UI;

namespace WinCheck.Models;

public class TreeNode
{
    public bool IsGroup { get; set; }
    public string Company { get; set; } = "";
    public int Count { get; set; }
    public long TotalMemory { get; set; }
    public string TotalMemoryFormatted => MainWindow.FormatBytes(TotalMemory);
    public ProcessRow? Process { get; set; }
    public ObservableCollection<TreeNode> Children { get; } = [];
}
