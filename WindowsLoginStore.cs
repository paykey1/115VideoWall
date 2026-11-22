using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VideoWall115;

internal sealed class LoginDetails(string account, string password)
{
    internal string Account { get; } = account;
    internal string Password { get; } = password;
}

internal sealed class WindowsLoginStore(string target = "115VideoWall/115.com")
{
    private const uint Generic = 1;
    private const uint ThisMachine = 2;
    private const int NotFound = 1168;

    internal void Save(LoginDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.Account) || string.IsNullOrEmpty(details.Password) ||
            details.Account.Length > 128 || details.Password.Length > 256 ||
            details.Account.Contains('\0') || details.Password.Contains('\0'))
            throw new ArgumentException("账号或密码格式不正确。");

        var secret = Marshal.StringToHGlobalUni(details.Password);
        try
        {
            var credential = new Credential
            {
                Type = Generic, TargetName = target, UserName = details.Account,
                CredentialBlobSize = checked((uint)details.Password.Length * 2),
                CredentialBlob = secret, Persist = ThisMachine,
            };
            if (!CredWriteW(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(secret);
        }
    }

    internal LoginDetails? Load()
    {
        if (!CredReadW(target, Generic, 0, out var buffer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound)
                return null;
            throw new Win32Exception(error);
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(buffer);
            if (string.IsNullOrEmpty(credential.UserName) || credential.CredentialBlob == IntPtr.Zero ||
                credential.CredentialBlobSize is 0 or > 512 || credential.CredentialBlobSize % 2 != 0)
                return null;
            var password = Marshal.PtrToStringUni(credential.CredentialBlob,
                (int)credential.CredentialBlobSize / 2);
            return string.IsNullOrEmpty(password) ? null : new LoginDetails(credential.UserName, password);
        }
        finally
        {
            CredFree(buffer);
        }
    }

    internal void Forget()
    {
        if (CredDeleteW(target, Generic, 0))
            return;
        var error = Marshal.GetLastWin32Error();
        if (error != NotFound)
            throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string? TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr buffer);
}
