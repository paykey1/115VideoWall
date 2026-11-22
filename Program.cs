namespace VideoWall115;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

#if DEBUG
        if (args.Length == 3 && args[0] == "--smoke")
            return SmokeTest.Run(new Uri(args[1]), args[2]);
        if (args.Length == 4 && args[0] == "--smoke-restart")
            return SmokeTest.VerifyRestart(new Uri(args[1]), args[2], args[3]);
        if (args.Length == 2 && args[0] == "--probe-official")
            return SmokeTest.CheckLanding(args[1]);
#endif
        var data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "115VideoWall");
        using var log = new RunLog(data);
        Application.ThreadException += (_, e) =>
        {
            log.Write("ui_error", new { errorType = e.Exception.GetType().Name });
            MessageBox.Show("操作失败。可查看本地日志，或重新打开程序。",
                "115 视频墙", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        using var main = new MainForm(data, log, loginStore: new WindowsLoginStore());
        Application.Run(main);
        return 0;
    }
}
