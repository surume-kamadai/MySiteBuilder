using System.Collections.ObjectModel;
using MySiteBuilder.Models;

namespace MySiteBuilder.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    // キャンバスに描画するすべてのノードのリスト（ObservableCollectionは追加・削除時にUIに変更を通知してくれます）
    public ObservableCollection<NodeData> Nodes { get; } = new();

    public MainWindowViewModel()
    {
        // テスト用に初期データをいくつか追加しておく
        Nodes.Add(new NodeData { Id = "Node1", X = 100, Y = 100, Width = 200, Height = 100 });
        Nodes.Add(new NodeData { Id = "Node2", X = 400, Y = 150, Width = 150, Height = 150 });
    }
}