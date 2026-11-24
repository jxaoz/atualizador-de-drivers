using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace DriverUpdater
{
    internal static class NvidiaPackage
    {
        private const int BufferSize = 128 * 1024;
        private const int InactivityTimeoutMilliseconds = 60000;
        private const string PublisherName = "NVIDIA Corporation";
        private const string InstallerName = "NVIDIA-Driver.exe";
        private static readonly Guid AuthenticodePolicy = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        public static bool IsDriverDownloadUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0)
                return false;

            string host = uri.IdnHost;
            return (host.Equals("download.nvidia.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".download.nvidia.com", StringComparison.OrdinalIgnoreCase)) &&
                uri.AbsolutePath.StartsWith("/Windows/", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<string> DownloadAsync(Uri uri, IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (!IsDriverDownloadUri(uri))
                throw new InvalidOperationException("O endereço não corresponde a um download oficial de driver da NVIDIA.");

            cancellationToken.ThrowIfCancellationRequested();
            string directory = Path.Combine(DownloadDirectory(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string partialPath = Path.Combine(directory, InstallerName + ".part");
            string packagePath = Path.Combine(directory, InstallerName);

            try
            {
                await DownloadFileAsync(uri, partialPath, progress, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partialPath, packagePath);
                using (FileStream package = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    VerifyPublisher(package, packagePath);

                cancellationToken.ThrowIfCancellationRequested();
                if (progress != null)
                    progress.Report(100);
                return packagePath;
            }
            catch
            {
                RemoveFailedDownload(partialPath, packagePath, directory);
                throw;
            }
        }

        public static void RunInstaller(string packagePath, params string[] publishers)
        {
            RunInstaller(packagePath, CancellationToken.None, publishers);
        }

        internal static void RunInstaller(string packagePath, CancellationToken cancellationToken, params string[] publishers)
        {
            string fullPath = Path.GetFullPath(packagePath);
            Process installer;
            using (FileStream package = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                VerifyPublisher(package, fullPath, publishers);
                cancellationToken.ThrowIfCancellationRequested();
                installer = Process.Start(new ProcessStartInfo
                {
                    FileName = fullPath,
                    WorkingDirectory = Path.GetDirectoryName(fullPath),
                    UseShellExecute = true
                });
            }

            if (installer == null)
                throw new InvalidOperationException("O Windows não iniciou o instalador do driver.");

            using (installer)
                installer.WaitForExit();
        }

        public static void Discard(string packagePath)
        {
            if (String.IsNullOrWhiteSpace(packagePath))
                return;

            try
            {
                string path = Path.GetFullPath(packagePath);
                string directory = Path.GetDirectoryName(path);
                Guid id;
                if (!String.Equals(Path.GetFileName(path), InstallerName, StringComparison.OrdinalIgnoreCase) ||
                    !Guid.TryParseExact(Path.GetFileName(directory), "N", out id) ||
                    !String.Equals(Path.GetDirectoryName(directory), DownloadDirectory(), StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    return;

                RemoveFailedDownload(path + ".part", path, directory);
            }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (NotSupportedException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string DownloadDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "DriversLicense", "NvidiaDownloads");
        }

        internal static async Task DownloadFileAsync(Uri uri, string path, IProgress<int> progress, CancellationToken cancellationToken,
            Func<Uri, bool> allowed = null, long maxBytes = 4L * 1024 * 1024 * 1024, string referer = null)
        {
            allowed = allowed ?? IsDriverDownloadUri;
            if (!allowed(uri))
                throw new InvalidOperationException("O endereço do pacote não é uma fonte oficial aceita.");
            for (int attempt = 0; attempt < 5; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpWebRequest request = WebRequest.CreateHttp(uri);
                request.AllowAutoRedirect = false;
                if (referer != null)
                    request.Referer = referer;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (idle.Token.Register(request.Abort))
                {
                    idle.CancelAfter(InactivityTimeoutMilliseconds);
                    try
                    {
                        using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                        {
                            int status = (int)response.StatusCode;
                            if (status >= 300 && status <= 399)
                            {
                                Uri target;
                                if (!Uri.TryCreate(uri, response.Headers[HttpResponseHeader.Location], out target) ||
                                    !allowed(target) || !string.Equals(uri.AbsolutePath, target.AbsolutePath, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidOperationException("O download foi redirecionado para um endereço não autorizado.");

                                uri = target;
                                continue;
                            }

                            if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > maxBytes)
                                throw new InvalidOperationException("O servidor não retornou um pacote completo dentro do tamanho aceito.");

                            using (Stream input = response.GetResponseStream())
                            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                                FileShare.None, BufferSize, FileOptions.Asynchronous))
                            {
                                if (input == null)
                                    throw new InvalidOperationException("O servidor retornou um download vazio.");

                                await CopyResponseAsync(input, output, response.ContentLength, progress, idle, maxBytes)
                                    .ConfigureAwait(false);
                            }
                            return;
                        }
                    }
                    catch (WebException error)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReportInactivity(idle, error);
                        throw new IOException("Não foi possível completar o download do pacote oficial.", error);
                    }
                    catch (IOException error)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReportInactivity(idle, error);
                        throw;
                    }
                    catch (OperationCanceledException error)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ReportInactivity(idle, error);
                        throw;
                    }
                }
            }

            throw new InvalidOperationException("O download excedeu o limite de redirecionamentos.");
        }

        private static void ReportInactivity(CancellationTokenSource idle, Exception error)
        {
            if (idle.IsCancellationRequested)
                throw new IOException("A conexão com o servidor ficou sem resposta durante o download.", error);
        }

        private static async Task CopyResponseAsync(Stream input, Stream output, long length,
            IProgress<int> progress, CancellationTokenSource idle, long maxBytes)
        {
            byte[] buffer = new byte[BufferSize];
            long received = 0;
            int lastProgress = 0;
            if (progress != null)
                progress.Report(0);

            int count;
            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, idle.Token).ConfigureAwait(false)) != 0)
            {
                idle.CancelAfter(InactivityTimeoutMilliseconds);
                received += count;
                if (received > maxBytes)
                    throw new IOException("O pacote excedeu o tamanho máximo aceito.");
                await output.WriteAsync(buffer, 0, count, idle.Token).ConfigureAwait(false);
                int percentage = length > 0 ? (int)Math.Min(99, received * 100.0 / length) : 0;
                if (progress != null && percentage != lastProgress)
                    progress.Report(percentage);
                lastProgress = percentage;
            }

            if (received == 0 || (length >= 0 && received != length))
                throw new IOException("O pacote foi recebido incompleto.");
        }

        private static void RemoveFailedDownload(string partialPath, string packagePath, string directory)
        {
            try
            {
                File.Delete(partialPath);
                File.Delete(packagePath);
                Directory.Delete(directory);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void VerifyPublisher(FileStream package, string path, params string[] publishers)
        {
            // Keep the verified bytes locked against writes until the caller has opened the installer.
            IntPtr nativePath = Marshal.StringToHGlobalUni(path);
            IntPtr fileAddress = IntPtr.Zero;
            try
            {
                fileAddress = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
                Marshal.StructureToPtr(new WinTrustFileInfo
                {
                    Size = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    Path = nativePath,
                    File = package.SafeFileHandle.DangerousGetHandle()
                }, fileAddress, false);

                var trust = new WinTrustData
                {
                    Size = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UIChoice = 2,          // WTD_UI_NONE
                    UnionChoice = 1,       // WTD_CHOICE_FILE
                    FileInfo = fileAddress,
                    StateAction = 1,       // WTD_STATEACTION_VERIFY
                    ProviderFlags = 0x6080 // Check chain revocation (excluding root), reject MD2/MD4, honor web-origin policy.
                };
                Guid policy = AuthenticodePolicy;
                try
                {
                    int status = WinVerifyTrust(new IntPtr(-1), ref policy, ref trust);
                    if (status != 0)
                        throw new InvalidOperationException("O Windows não validou a assinatura do instalador (0x" +
                            unchecked((uint)status).ToString("X8") + ").");

                    string signer = ReadVerifiedPublisher(trust.State);
                    string[] accepted = publishers.Length == 0 ? new[] { PublisherName } : publishers;
                    if (!Array.Exists(accepted, name => String.Equals(signer, name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("A assinatura validada não pertence ao fabricante esperado.");
                }
                finally
                {
                    trust.StateAction = 2; // WTD_STATEACTION_CLOSE
                    WinVerifyTrust(new IntPtr(-1), ref policy, ref trust);
                }
            }
            finally
            {
                if (fileAddress != IntPtr.Zero)
                    Marshal.FreeHGlobal(fileAddress);
                Marshal.FreeHGlobal(nativePath);
            }
        }

        private static string ReadVerifiedPublisher(IntPtr state)
        {
            IntPtr provider = WTHelperProvDataFromStateData(state);
            if (provider == IntPtr.Zero)
                return null;

            IntPtr signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == IntPtr.Zero)
                return null;

            IntPtr certificate = WTHelperGetProvCertFromChain(signer, 0);
            if (certificate == IntPtr.Zero)
                return null;

            var header = (ProviderCertificateHeader)Marshal.PtrToStructure(certificate, typeof(ProviderCertificateHeader));
            if (header.Certificate == IntPtr.Zero)
                return null;

            using (var publisher = new X509Certificate2(header.Certificate))
                return publisher.GetNameInfo(X509NameType.SimpleName, false);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustFileInfo
        {
            public uint Size;
            public IntPtr Path;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public uint Size;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UIChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInfo;
            public uint StateAction;
            public IntPtr State;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UIContext;
            public IntPtr SignatureSettings;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProviderCertificateHeader
        {
            public uint Size;
            public IntPtr Certificate;
        }

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index,
            [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

        [DllImport("wintrust.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
    }
}
