namespace VideoWall115;

internal static class Palette
{
    internal static readonly Color Canvas = Color.FromArgb(16, 21, 28);
    internal static readonly Color Panel = Color.FromArgb(27, 35, 46);
    internal static readonly Color ButtonFill = Color.FromArgb(42, 55, 70);
    internal static readonly Color Text = Color.FromArgb(237, 243, 250);
    internal static readonly Color Muted = Color.FromArgb(163, 179, 197);

    internal static Button Button(string text, string description)
    {
        var button = new Button
        {
            Text = text, AccessibleName = description, BackColor = ButtonFill, ForeColor = Text,
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}
