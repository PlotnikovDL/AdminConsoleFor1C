using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace AdminConsoleFor1C.Infrastructure.Services;

[SupportedOSPlatform("windows")]
internal sealed class WindowsServiceLogon : IDisposable
{
    private const string ServiceLogonRight = "SeServiceLogonRight";
    private readonly byte[] sidBytes;
    private IntPtr policy;
    private bool addedRight;
    private bool completed;

    public SecurityIdentifier Sid { get; }
    public string AccountName { get; }

    private WindowsServiceLogon(string accountName)
    {
        AccountName = accountName.StartsWith(@".\", StringComparison.Ordinal)
            ? Environment.MachineName + accountName[1..] : accountName;
        Sid = (SecurityIdentifier)new NTAccount(AccountName).Translate(typeof(SecurityIdentifier));
        sidBytes = new byte[Sid.BinaryLength];
        Sid.GetBinaryForm(sidBytes, 0);
    }

    public static WindowsServiceLogon Prepare(string accountName, string password)
    {
        var logon = new WindowsServiceLogon(accountName);
        try
        {
            var attributes = new LsaObjectAttributes { Length = (uint)Marshal.SizeOf<LsaObjectAttributes>() };
            const uint policyViewLocalInformation = 0x0001;
            const uint policyCreateAccount = 0x0010;
            const uint policyLookupNames = 0x0800;
            CheckStatus(LsaOpenPolicy(IntPtr.Zero, ref attributes,
                policyViewLocalInformation | policyCreateAccount | policyLookupNames, out logon.policy));
            if (!logon.HasServiceLogonRight())
            {
                logon.ChangeRight(add: true);
                logon.addedRight = true;
            }

            var separator = logon.AccountName.IndexOf('\\');
            var domain = separator >= 0 ? logon.AccountName[..separator] : null;
            var user = separator >= 0 ? logon.AccountName[(separator + 1)..] : logon.AccountName;
            const int logon32LogonService = 5;
            if (!LogonUser(user, domain, password, logon32LogonService, 0, out var token))
            {
                var error = new Win32Exception(Marshal.GetLastWin32Error());
                token?.Dispose();
                throw new InvalidOperationException($"Вход от имени «{logon.AccountName}» не выполнен. {error.Message}", error);
            }
            token.Dispose();
            return logon;
        }
        catch
        {
            logon.Dispose();
            throw;
        }
    }

    public void Complete() => completed = true;

    private bool HasServiceLogonRight()
    {
        var status = LsaEnumerateAccountRights(policy, sidBytes, out var rights, out var count);
        if (status == 0xC0000034) // STATUS_OBJECT_NAME_NOT_FOUND: no rights assigned directly.
            return false;
        CheckStatus(status);
        try
        {
            for (var index = 0; index < count; index++)
            {
                var right = Marshal.PtrToStructure<LsaUnicodeString>(IntPtr.Add(rights, index * Marshal.SizeOf<LsaUnicodeString>()));
                if (Marshal.PtrToStringUni(right.Buffer, right.Length / 2) == ServiceLogonRight)
                    return true;
            }
            return false;
        }
        finally
        {
            LsaFreeMemory(rights);
        }
    }

    private void ChangeRight(bool add)
    {
        var buffer = Marshal.StringToHGlobalUni(ServiceLogonRight);
        try
        {
            var right = new LsaUnicodeString
            {
                Buffer = buffer, Length = (ushort)(ServiceLogonRight.Length * 2),
                MaximumLength = (ushort)((ServiceLogonRight.Length + 1) * 2)
            };
            CheckStatus(add ? LsaAddAccountRights(policy, sidBytes, [right], 1)
                : LsaRemoveAccountRights(policy, sidBytes, false, [right], 1));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (policy == IntPtr.Zero)
            return;
        try
        {
            if (addedRight && !completed)
                ChangeRight(add: false);
        }
        finally
        {
            LsaClose(policy);
            policy = IntPtr.Zero;
        }
    }

    private static void CheckStatus(uint status)
    {
        if (status != 0)
            throw new Win32Exception((int)LsaNtStatusToWinError(status));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("advapi32.dll")] private static extern uint LsaOpenPolicy(IntPtr system, ref LsaObjectAttributes attributes, uint access, out IntPtr policy);
    [DllImport("advapi32.dll")] private static extern uint LsaEnumerateAccountRights(IntPtr policy, byte[] sid, out IntPtr rights, out int count);
    [DllImport("advapi32.dll")] private static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, LsaUnicodeString[] rights, uint count);
    [DllImport("advapi32.dll")] private static extern uint LsaRemoveAccountRights(IntPtr policy, byte[] sid, [MarshalAs(UnmanagedType.U1)] bool allRights, LsaUnicodeString[] rights, uint count);
    [DllImport("advapi32.dll")] private static extern uint LsaNtStatusToWinError(uint status);
    [DllImport("advapi32.dll")] private static extern uint LsaFreeMemory(IntPtr buffer);
    [DllImport("advapi32.dll")] private static extern uint LsaClose(IntPtr policy);
    [DllImport("advapi32.dll", EntryPoint = "LogonUserW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUser(string user, string? domain, string password, int type, int provider, out SafeAccessTokenHandle token);
}
