using System.Diagnostics;
using FramePathLab.Core.Abstractions;

namespace FramePathLab.Windows.Power;

public sealed class ProcessPowerSessionGuardian(
    string executablePath,
    PowerSessionJournalStore journalStore) : IPowerSessionGuardian
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GuardianProcess> _guardians = [];

    public void Arm(Guid sessionId, Guid nonce, int ownerProcessId)
    {
        var fullExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullExecutablePath))
        {
            throw new FileNotFoundException("The guardian executable could not be resolved.", fullExecutablePath);
        }

        if (File.GetAttributes(fullExecutablePath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new IOException("The guardian executable cannot be launched through a reparse point.");
        }

        var acknowledgementPath = journalStore.GetGuardianAckPath(sessionId);
        if (File.Exists(acknowledgementPath))
        {
            if (File.GetAttributes(acknowledgementPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The guardian acknowledgement path is a reparse point.");
            }

            File.Delete(acknowledgementPath);
        }

        var startInfo = new ProcessStartInfo(fullExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--power-guardian");
        startInfo.ArgumentList.Add(sessionId.ToString("D"));
        startInfo.ArgumentList.Add(nonce.ToString("D"));
        startInfo.ArgumentList.Add(ownerProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the rollback guardian.");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"The rollback guardian exited before arming (code {process.ExitCode}).");
                }

                if (File.Exists(acknowledgementPath))
                {
                    if (File.GetAttributes(acknowledgementPath).HasFlag(FileAttributes.ReparsePoint))
                    {
                        throw new IOException("The guardian acknowledgement is a reparse point.");
                    }

                    var acknowledgementInfo = new FileInfo(acknowledgementPath);
                    if (acknowledgementInfo.Length is <= 0 or > 128)
                    {
                        throw new InvalidDataException("The rollback guardian acknowledgement has an invalid size.");
                    }

                    var acknowledgement = File.ReadAllText(acknowledgementPath).Trim();
                    if (Guid.TryParse(acknowledgement, out var acknowledgedNonce)
                        && acknowledgedNonce == nonce)
                    {
                        if (process.HasExited)
                        {
                            throw new InvalidOperationException($"The rollback guardian exited while arming (code {process.ExitCode}).");
                        }

                        lock (_gate)
                        {
                            if (_guardians.Remove(sessionId, out var previous))
                            {
                                previous.Process.Dispose();
                            }

                            _guardians.Add(sessionId, new GuardianProcess(process, nonce, acknowledgementPath));
                        }

                        return;
                    }

                    throw new InvalidDataException("The rollback guardian acknowledgement did not match the prepared transaction.");
                }

                Thread.Sleep(100);
            }

            throw new TimeoutException("The rollback guardian did not acknowledge the transaction in time.");
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public bool IsArmed(Guid sessionId)
    {
        lock (_gate)
        {
            if (!_guardians.TryGetValue(sessionId, out var guardian))
            {
                return false;
            }

            try
            {
                if (guardian.Process.HasExited
                    || !File.Exists(guardian.AcknowledgementPath)
                    || File.GetAttributes(guardian.AcknowledgementPath).HasFlag(FileAttributes.ReparsePoint))
                {
                    RemoveGuardian(sessionId, guardian);
                    return false;
                }

                var acknowledgement = File.ReadAllText(guardian.AcknowledgementPath).Trim();
                if (!Guid.TryParse(acknowledgement, out var nonce) || nonce != guardian.Nonce)
                {
                    RemoveGuardian(sessionId, guardian);
                    return false;
                }

                return true;
            }
            catch
            {
                RemoveGuardian(sessionId, guardian);
                return false;
            }
        }
    }

    private void RemoveGuardian(Guid sessionId, GuardianProcess guardian)
    {
        _guardians.Remove(sessionId);
        guardian.Process.Dispose();
    }

    private sealed record GuardianProcess(Process Process, Guid Nonce, string AcknowledgementPath);
}
