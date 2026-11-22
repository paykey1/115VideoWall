namespace VideoWall115;

internal sealed class PlayerWindow : BorderlessWindow
{
    internal int PlacementSlot { get; }

    internal PlayerWindow(Form owner, int id, int placementSlot)
    {
        Owner = owner;
        PlacementSlot = placementSlot;
        Text = $"115 视频墙 · 视频 {id}";
        BackColor = Color.Black;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(840, 510);
        MinimumSize = new Size(360, 210);
    }
}
